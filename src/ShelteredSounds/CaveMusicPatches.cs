using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace ShelteredSounds;

// Limit every patch to CaveMusicTrack: normal music, weather, creatures, machines,
// temporal effects, and other ambient sounds retain their vanilla behavior.
internal static class CaveMusicPatches
{
    internal static Func<bool> IsSheltered = static () => false;

    [HarmonyPatch(typeof(CaveMusicTrack), nameof(CaveMusicTrack.ShouldPlay))]
    private static class ShouldPlayPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!IsSheltered()) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(CaveMusicTrack), nameof(CaveMusicTrack.ContinuePlay))]
    private static class ContinuePlayPatch
    {
        private static bool Prefix(CaveMusicTrack __instance, ref bool __result)
        {
            if (!IsSheltered()) return true;

            // Returning false releases the engine's current cave track. Its normal
            // selection logic can then choose another eligible song, or stay silent.
            __instance.FadeOut(0.5f);
            __result = false;
            return false;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo load = AccessTools.Method(typeof(IMusicEngine), nameof(IMusicEngine.LoadTrack));
            MethodInfo guardedLoad = AccessTools.Method(typeof(CaveMusicPatches), nameof(LoadCaveTrack));
            int replacements = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(load))
                {
                    // The helper takes the original instance as its first argument,
                    // preserving the stack, labels, and exception blocks of this call.
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = guardedLoad;
                    replacements++;
                }
                yield return instruction;
            }

            if (replacements != 1)
            {
                throw new InvalidOperationException("Sheltered Sounds: expected one cave LoadTrack call. This game version is incompatible.");
            }
        }
    }

    internal static void LoadCaveTrack(IMusicEngine engine, AssetLocation location,
        Action<ILoadedSound> onLoaded, float volume, float pitch)
    {
        engine.LoadTrack(location, sound =>
        {
            // A request made outside can finish after the player has entered a room.
            // Vanilla's callback starts the sound unconditionally if it is non-null.
            if (IsSheltered())
            {
                sound?.Dispose();
                onLoaded(null!); // Vanilla uses null to report a failed/cancelled load.
                return;
            }
            onLoaded(sound);
        }, volume, pitch);
    }
}
