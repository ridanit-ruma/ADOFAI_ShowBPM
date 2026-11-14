using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ShowBPM
{
    /// <summary>
    /// Name-based access to the game's types and members.
    /// The game renames, moves and deletes types between releases, and a direct
    /// reference to a missing type makes the whole mod fail to load. Resolving
    /// everything at runtime keeps one missing member from breaking the rest.
    /// </summary>
    internal static class Game
    {
        private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic |
                                              BindingFlags.Instance | BindingFlags.Static |
                                              BindingFlags.DeclaredOnly;

        private static readonly Dictionary<string, Type> types = new Dictionary<string, Type>();

        private static readonly Member controller = new Member("ADOBase", "controller");
        private static readonly Member conductor = new Member("ADOBase", "conductor");
        private static readonly Member gameworld = new Member("scrController", "gameworld");
        private static readonly Member speed = new Member("scrController", "speed");
        private static readonly Member currentSeqID = new Member("scrController", "currentSeqID");
        private static readonly Member averageFrameTime = new Member("scrController", "averageFrameTime");
        private static readonly Member conductorBpm = new Member("scrConductor", "bpm");
        private static readonly Member song = new Member("scrConductor", "song");
        private static readonly Member nextFloor = new Member("scrFloor", "nextfloor");
        private static readonly Member entryAngle = new Member("scrFloor", "entryangle");
        private static readonly Member exitAngle = new Member("scrFloor", "exitangle");
        private static readonly Member entryTime = new Member("scrFloor", "entryTime");
        private static readonly Member isCCW = new Member("scrFloor", "isCCW");
        private static readonly Member language = new Member("RDString", "language");
        private static readonly Member fontDataFont = new Member(null, "font");

        private static readonly Method getAngleMoved = new Method("scrMisc", "GetAngleMoved", 3);
        private static readonly Method angleToTime = new Method("scrMisc", "AngleToTime", 2);
        private static readonly Method getFontData = new Method("RDString", "GetFontDataForLanguage", 1);

        public static object Controller => controller.Get(null);
        public static object Conductor => conductor.Get(null);

        public static bool Gameworld => Convert.ToBoolean(gameworld.Get(Controller));
        public static double Speed => Convert.ToDouble(speed.Get(Controller));
        public static int CurrentSeqID => Convert.ToInt32(currentSeqID.Get(Controller));
        public static double AverageFrameTime => Convert.ToDouble(averageFrameTime.Get(Controller));

        public static float ConductorBpm => Convert.ToSingle(conductorBpm.Get(Conductor));
        public static float SongPitch => ((AudioSource) song.Get(Conductor)).pitch;

        public static object NextFloor(object floor) => nextFloor.Get(floor);
        public static double EntryAngle(object floor) => Convert.ToDouble(entryAngle.Get(floor));
        public static double ExitAngle(object floor) => Convert.ToDouble(exitAngle.Get(floor));
        public static double EntryTime(object floor) => Convert.ToDouble(entryTime.Get(floor));
        public static bool IsCCW(object floor) => Convert.ToBoolean(isCCW.Get(floor));

        public static double GetAngleMoved(double entry, double exit, bool clockwise) =>
            Convert.ToDouble(getAngleMoved.Invoke(entry, exit, clockwise));

        public static double AngleToTime(double angle, double bpm) =>
            Convert.ToDouble(angleToTime.Invoke(angle, bpm));

        public static SystemLanguage Language => (SystemLanguage) language.Get(null);

        public static Font GetFont(SystemLanguage lang) =>
            (Font) fontDataFont.Get(getFontData.Invoke(lang));

        /// <summary>Finds a game type by name, or null if this game version does not have it.</summary>
        public static Type FindType(string name)
        {
            if (types.TryGetValue(name, out Type cached)) return cached;

            Type found = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type candidate;
                try
                {
                    candidate = assembly.GetType(name, false);
                }
                catch (Exception)
                {
                    continue;
                }

                if (candidate == null) continue;
                found = candidate;
                if (assembly.GetName().Name == "Assembly-CSharp") break;
            }

            types[name] = found;
            return found;
        }

        /// <summary>Finds every method with the given name on a type or, failing that, its base types.</summary>
        public static List<MethodInfo> FindMethods(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                List<MethodInfo> methods = t.GetMethods(Declared).Where(m => m.Name == name).ToList();
                if (methods.Count > 0) return methods;
            }

            return new List<MethodInfo>();
        }

        /// <summary>A field or property that is looked up by name on first use.</summary>
        private sealed class Member
        {
            private readonly string typeName;
            private readonly string name;
            private FieldInfo field;
            private PropertyInfo property;

            public Member(string typeName, string name)
            {
                this.typeName = typeName;
                this.name = name;
            }

            public object Get(object target)
            {
                if (field == null && property == null) Resolve(target);
                return field != null ? field.GetValue(field.IsStatic ? null : target) : property.GetValue(target, null);
            }

            private void Resolve(object target)
            {
                Type start = target != null ? target.GetType() : FindType(typeName);
                for (Type t = start; t != null; t = t.BaseType)
                {
                    field = t.GetField(name, Declared);
                    if (field != null) return;
                    property = t.GetProperty(name, Declared);
                    if (property != null) return;
                }

                throw new MissingMemberException(start?.Name ?? typeName, name);
            }
        }

        /// <summary>A static method that is looked up by name and parameter count on first use.</summary>
        private sealed class Method
        {
            private readonly string typeName;
            private readonly string name;
            private readonly int parameterCount;
            private MethodInfo method;
            private Type[] parameterTypes;

            public Method(string typeName, string name, int parameterCount)
            {
                this.typeName = typeName;
                this.name = name;
                this.parameterCount = parameterCount;
            }

            public object Invoke(params object[] args)
            {
                if (method == null) Resolve();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] is IConvertible && !parameterTypes[i].IsInstanceOfType(args[i]) && !parameterTypes[i].IsEnum)
                        args[i] = Convert.ChangeType(args[i], parameterTypes[i]);
                }

                return method.Invoke(null, args);
            }

            private void Resolve()
            {
                Type type = FindType(typeName);
                method = type == null
                    ? null
                    : FindMethods(type, name).FirstOrDefault(m => m.IsStatic && m.GetParameters().Length == parameterCount);
                if (method == null) throw new MissingMethodException(typeName, name);
                parameterTypes = method.GetParameters().Select(p => p.ParameterType).ToArray();
            }
        }
    }
}
