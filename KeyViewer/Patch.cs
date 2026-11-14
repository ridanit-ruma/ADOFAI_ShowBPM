using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace ShowBPM
{
    internal static class Patch
    {
        private static float bpm = 0, pitch = 0, playbackSpeed = 1;
        private static bool first = true, beforedt = false;
        private static double beforebpm = 0;
        private static readonly HashSet<string> reported = new HashSet<string>();

        /// <summary>
        /// Applies every patch on its own, so a type or method that this game
        /// version does not have only disables the patch that needs it.
        /// </summary>
        internal static void Apply(Harmony harmony)
        {
            Add(harmony, "scrCalibrationPlanet", "Start", postfix: nameof(HideText));
            Add(harmony, "scrUIController", "WipeToBlack", postfix: nameof(HideText));
            Add(harmony, "scnEditor", "ResetScene", postfix: nameof(HideText));
            Add(harmony, "scrController", "StartLoadingScene", postfix: nameof(HideText));
            Add(harmony, "scrController", "Awake", prefix: nameof(ControllerAwake));
            Add(harmony, "scnGame", "Play", postfix: nameof(LevelStarted));
            Add(harmony, "scrPressToStart", "ShowText", postfix: nameof(LevelStarted));
            Add(harmony, "scrPlanet", "MoveToNextFloor", postfix: nameof(MoveToNextFloor));
            Add(harmony, "RDString", "ChangeLanguage", prefix: nameof(ChangeLanguage));
        }

        private static void Add(Harmony harmony, string typeName, string methodName, string prefix = null,
            string postfix = null)
        {
            try
            {
                Type type = Game.FindType(typeName);
                List<MethodInfo> targets = type == null ? new List<MethodInfo>() : Game.FindMethods(type, methodName);
                if (targets.Count == 0)
                {
                    Main.Logger.Log($"Skipped patch for {typeName}.{methodName}: not found in this game version.");
                    return;
                }

                foreach (MethodInfo target in targets)
                {
                    // Run before other mods' patches on the same method: a patch that throws
                    // there would otherwise keep ours from running at all.
                    harmony.Patch(target,
                        prefix == null ? null : new HarmonyMethod(typeof(Patch), prefix) {priority = Priority.First},
                        postfix == null ? null : new HarmonyMethod(typeof(Patch), postfix) {priority = Priority.First});
                }
            }
            catch (Exception e)
            {
                Main.Logger.Error($"Failed to patch {typeName}.{methodName}.");
                Main.Logger.LogException(e);
            }
        }

        // Logs each distinct failure once instead of on every tile.
        private static void Report(Exception e)
        {
            if (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
            if (!reported.Add(e.GetType().Name + e.Message)) return;
            Main.Logger.LogException(e);
        }

        // Unity objects compare equal to null once destroyed, which a plain object comparison misses.
        private static bool IsNull(object obj)
        {
            if (obj is UnityEngine.Object unityObject) return unityObject == null;
            return obj == null;
        }

        private static void HideText()
        {
            if (!Main.IsEnabled) return;
            Main.gui.Show(false);
        }

        private static void ControllerAwake()
        {
            try
            {
                if (first)
                {
                    first = false;
                    Main.gui.text.font = Game.GetFont(Game.Language);
                }

                if (!Main.IsEnabled) return;
                SetLanguage(Game.Language);
            }
            catch (Exception e)
            {
                Report(e);
            }
        }

        private static void LevelStarted()
        {
            try
            {
                if (!Main.IsEnabled) return;
                if (!Game.Gameworld) return;

                LevelStart();
            }
            catch (Exception e)
            {
                Report(e);
            }
        }

        private static void MoveToNextFloor(object floor)
        {
            try
            {
                if (!Main.IsEnabled) return;
                if (!Game.Gameworld) return;
                object next = Game.NextFloor(floor);
                if (IsNull(next)) return;
                List<string> texts = new List<string>();

                double speed = 0, kps = 0, curBPM = 0, nextbpm = 0;
                speed = Game.Speed;
                curBPM = getRealBPM(floor, bpm) * playbackSpeed * pitch;
                nextbpm = getRealBPM(next, bpm) * playbackSpeed * pitch;

                bool isDongta = false;
                if (Main.setting.ignoreMultipress)
                {
                    bool applyMultipressDamage = false;
                    bool flag2 = true;
                    double angleMoved = Game.GetAngleMoved(Game.EntryAngle(floor), Game.ExitAngle(floor),
                        !Game.IsCCW(floor));
                    double time = Game.AngleToTime(angleMoved, (double) Game.ConductorBpm * speed);
                    double num2 = 1.56905098538846;
                    bool flag3 = flag2 & angleMoved > num2;
                    double num3 = Game.AverageFrameTime * 2.5;
                    bool flag4 = flag3 & time > num3;
                    double num4 = 0.0299999993294477;
                    applyMultipressDamage = flag4 & time > num4;

                    isDongta = !applyMultipressDamage && !doubleEqual(nextbpm, curBPM);
                }

                if (Main.setting.onTileBpm)
                    texts.Add(Main.setting.text1.Replace("{value}", format((float) (bpm * speed))));
                if (isDongta || beforedt) curBPM = beforebpm;

                if (Main.setting.onCurBpm) texts.Add(Main.setting.text2.Replace("{value}", format((float) curBPM)));
                if (Main.setting.onRecommandKPS)
                {
                    kps = curBPM / 60;
                    texts.Add(Main.setting.text3.Replace("{value}", Math.Round(kps).ToString()));
                }

                Main.gui.setText(string.Join("\n", texts));

                beforedt = isDongta;
                beforebpm = curBPM;
            }
            catch (Exception e)
            {
                Report(e);
            }
        }

        private static void ChangeLanguage(SystemLanguage language)
        {
            try
            {
                SetLanguage(language);
                Main.gui.text.font = Game.GetFont(language);
            }
            catch (Exception e)
            {
                Report(e);
            }
        }

        private static void SetLanguage(SystemLanguage language)
        {
            Main.language = Main.languages.ContainsKey(language.ToString())
                ? Main.languages[language.ToString()]
                : Main.languages["English"];
        }

        public static bool doubleEqual(double f1, double f2)
        {
            return Math.Abs(f1 - f2) < 0.0001;
        }

        public static double getRealBPM(object floor, float bpm)
        {
            if (IsNull(floor))
                return bpm;
            object next = Game.NextFloor(floor);
            if (IsNull(next))
                return Game.Speed * bpm;
            return 60.0 / (Game.EntryTime(next) - Game.EntryTime(floor));
        }

        public static string Repeat(string value, int count)
        {
            return new StringBuilder(value.Length * count).Insert(0, value, count).ToString();
        }

        public static string format(float v)
        {
            return string.Format("{0:0." + Repeat(Main.setting.zero ? "0" : "#", Main.setting.showDecimal) + "}", v);
        }

        public static void LevelStart()
        {
            Main.gui.Show(true);

            List<string> texts = new List<string>();
            float kps = 0;

            pitch = Game.SongPitch;
            playbackSpeed = 1;
            bpm = Game.ConductorBpm * pitch;

            float cur = bpm;
            if (Game.CurrentSeqID != 0)
            {
                cur = (float) (bpm * Game.Speed);
            }

            if (Main.setting.onTileBpm)
                texts.Add(Main.setting.text1.Replace("{value}", format(cur)));
            if (Main.setting.onCurBpm) texts.Add(Main.setting.text2.Replace("{value}", format(cur)));

            if (Main.setting.onRecommandKPS)
            {
                kps = cur / 60;
                texts.Add(Main.setting.text3.Replace("{value}", Math.Round(kps).ToString()));
            }

            Main.gui.setText(string.Join("\n", texts));
            Main.gui.setSize(Main.setting.size);
        }
    }
}
