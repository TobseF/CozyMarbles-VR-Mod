using System;
using System.Reflection;
using UnityEngine;

namespace CozyMarblesVR
{
    /// <summary>Texts of the mod: German when the game language is German, English for every other language.</summary>
    public static class Loc
    {
        static bool _german;
        static float _next = -10f;
        static Type _manager;
        static bool _searched;

        public static bool German
        {
            get
            {
                if (Time.realtimeSinceStartup < _next) return _german;
                _next = Time.realtimeSinceStartup + 1f;
                try
                {
                    if (!_searched)
                    {
                        _searched = true;
                        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            _manager = a.GetType("TimR.Localization.LocaManager");
                            if (_manager != null) break;
                        }
                    }
                    if (_manager != null)
                    {
                        object lang = null;
                        var inst = _manager.GetProperty("_", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                        if (inst != null) lang = _manager.GetProperty("ActiveLanguage")?.GetValue(inst);
                        if (lang == null) lang = _manager.GetMethod("GetSavedLanguage", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                        if (lang != null) { _german = lang.ToString() == "German"; return _german; }
                    }
                    _german = Application.systemLanguage == SystemLanguage.German;
                }
                catch { _german = Application.systemLanguage == SystemLanguage.German; }
                return _german;
            }
        }

        public static string T(string de, string en) => German ? de : en;
    }
}
