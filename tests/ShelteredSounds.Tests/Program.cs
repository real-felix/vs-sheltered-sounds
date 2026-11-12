using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using HarmonyLib;
using ShelteredSounds;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

internal static class Program
{
    private static void Main()
    {
        string gamePath = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "VintageStoryPath").Value!;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            foreach (string directory in new[] { gamePath, Path.Combine(gamePath, "Lib"), Path.Combine(gamePath, "Mods") })
            {
                string path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
            }
            return null;
        };
        RunGameAssemblyChecks();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunGameAssemblyChecks()
    {
        // Assembly names and display names are not dependency IDs: VSEssentials
        // registers as "game". Check metadata against the actual installed mod.
        var essentials = typeof(RoomRegistry).Assembly.GetCustomAttribute<ModInfoAttribute>()!;
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "modinfo.json")));
        var dependencies = metadata.RootElement.GetProperty("dependencies");
        Check(dependencies.TryGetProperty(essentials.ModID, out _)
            && dependencies.EnumerateObject().All(dependency =>
                dependency.Name == essentials.ModID && dependency.Value.GetString() == essentials.Version),
            "release dependencies match the installed Essentials mod ID and version");

        var harmony = new Harmony("shelteredsounds.tests");
        bool previousCaveMusic = CaveMusicTrack.ShouldPlayCaveMusic;
        Room? room = null;
        CaveMusicPatches.IsSheltered = () => ShelteredSoundsSystem.IsEnclosedRoom(room);
        try
        {
            // These are actual runtime patches against the installed game, including
            // the transpiler. A signature/IL mismatch must fail this check.
            harmony.PatchAll(typeof(ShelteredSoundsSystem).Assembly);
            CaveMusicTrack.ShouldPlayCaveMusic = true;
            var world = Stub<IClientWorldAccessor>.Create((method, _) => method.Name switch
            {
                "get_ElapsedMilliseconds" => 1000L,
                _ => throw new NotSupportedException(method.Name)
            });
            var api = Stub<ICoreClientAPI>.Create((method, _) => method.Name switch
            {
                "get_World" => world,
                _ => throw new NotSupportedException(method.Name)
            });
            var props = new TrackedPlayerProperties { sunSlight = 0 };
            var position = new BlockPos(0, 50, 0);
            var conditions = new ClimateCondition();
            var part = new MusicTrackPart { Files = [new AssetLocation("game:music/cave/test.ogg")] };
            var track = new CaveMusicTrack();
            SetField(track, "capi", api);
            SetField(track, "Parts", new[] { part });
            SetField(track, "PartsShuffled", new[] { part });

            Check(track.ShouldPlay(props, conditions, position), "cave remains eligible outdoors in darkness");
            props.sunSlight = 15;
            Check(!track.ShouldPlay(props, conditions, position), "vanilla sunlight rule remains intact");
            props.sunSlight = 0;
            CaveMusicTrack.ShouldPlayCaveMusic = false;
            Check(!track.ShouldPlay(props, conditions, position), "vanilla temporal music switch remains intact");
            CaveMusicTrack.ShouldPlayCaveMusic = true;

            room = new Room { ExitCount = 0, IsSmallRoom = false, SkylightCount = 0, NonSkylightCount = 20 };
            Check(!track.ShouldPlay(props, conditions, position), "large dark enclosed room blocks cave mix");
            room.IsSmallRoom = true;
            Check(!track.ShouldPlay(props, conditions, position), "cellar blocks cave mix");
            room.SkylightCount = 20;
            room.NonSkylightCount = 0;
            Check(!track.ShouldPlay(props, conditions, position), "greenhouse blocks cave mix");
            room.ExitCount = 1;
            Check(track.ShouldPlay(props, conditions, position), "open cave is not shelter");
            room.ExitCount = 0;
            room.AnyChunkUnloaded = 1;
            Check(track.ShouldPlay(props, conditions, position), "incomplete room data does not establish shelter");

            Action<ILoadedSound>? pending = null;
            var engine = Stub<IMusicEngine>.Create((method, args) =>
            {
                if (method.Name != "LoadTrack") throw new NotSupportedException(method.Name);
                pending = (Action<ILoadedSound>)args![1]!;
                return null;
            });
            SetField(track, "musicEngine", engine);
            room = null;
            track.BeginPlay(props);
            Check(track.ContinuePlay(1, props) && part.Loading && pending != null, "outdoor cave mix queues a real part");

            int starts = 0;
            int disposals = 0;
            int fades = 0;
            bool playing = false;
            var sound = Stub<ILoadedSound>.Create((method, args) =>
            {
                switch (method.Name)
                {
                    case "Start": starts++; playing = true; return null;
                    case "Dispose": disposals++; playing = false; return null;
                    case "get_IsPlaying": return playing;
                    case "FadeOut": fades++; ((Action<ILoadedSound>)args![1]!)(soundForCallback!); return null;
                    default: throw new NotSupportedException(method.Name);
                }
            });
            soundForCallback = sound;

            // Cross the doorway while the actual vanilla callback is still pending.
            room = new Room { ExitCount = 0 };
            pending!(sound);
            Check(starts == 0 && disposals == 1 && !part.Loading && part.Sound == null,
                "late cave load is disposed without starting and vanilla clears its loading flag");

            room = null;
            pending = null;
            Check(track.ContinuePlay(1, props) && pending != null, "leaving shelter allows cave loading again");
            pending!(sound);
            Check(starts == 1 && part.Sound == sound && !part.Loading, "outdoor callback starts the vanilla cave part");
            room = new Room { ExitCount = 0 };
            Check(!track.ContinuePlay(1, props) && fades == 1 && part.Sound == null,
                "entering shelter fades existing cave audio and releases the track");

            Check(new ShelteredSoundsSystem().ShouldLoad(EnumAppSide.Client)
                && !new ShelteredSoundsSystem().ShouldLoad(EnumAppSide.Server), "mod loads only on clients");
            Console.WriteLine("All game-assembly checks passed.");
        }
        finally
        {
            CaveMusicPatches.IsSheltered = static () => false;
            CaveMusicTrack.ShouldPlayCaveMusic = previousCaveMusic;
            harmony.UnpatchAll(harmony.Id);
            soundForCallback = null;
        }
    }

    private static ILoadedSound? soundForCallback;

    private static void SetField(object target, string field, object value) =>
        AccessTools.Field(target.GetType(), field).SetValue(target, value);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}

public class Stub<T> : DispatchProxy where T : class
{
    public System.Func<MethodInfo, object?[]?, object?> Handler = null!;

    public static T Create(System.Func<MethodInfo, object?[]?, object?> handler)
    {
        T result = Create<T, Stub<T>>();
        ((Stub<T>)(object)result).Handler = handler;
        return result;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
}
