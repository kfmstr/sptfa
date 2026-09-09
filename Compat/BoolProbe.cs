using System;
using System.Reflection;

namespace SPTFreeAim.Compat
{
    /// <summary>
    /// A boolean the game exposes, whose member name we are not certain of.
    ///
    /// The stance suspensions (sprinting, reloading, using an item) need state
    /// that neither reference mod exposes under a name we could verify. Rather
    /// than hard-code a guess that silently returns false forever, a probe tries
    /// an ordered list of candidate names, remembers which one resolved, and
    /// reports it at startup:
    ///
    ///   sprint          -> MovementContext.IsSprintEnabled
    ///   reload          -> UNRESOLVED (tried: IsInReloadOperation, IsReloading, ...)
    ///
    /// An unresolved probe reads as <c>false</c> and says so once. That means a
    /// missing name degrades to "free aim stays on during a reload" - visibly
    /// wrong, harmless, and named in the log - rather than to a crash or to a
    /// silent behaviour change nobody can trace.
    ///
    /// If a probe comes back UNRESOLVED, the name is in the dnSpy export. See
    /// docs/08-RECON.md R9.
    /// </summary>
    public class BoolProbe
    {
        private readonly string _label;
        private readonly string[] _candidates;

        private PropertyInfo _prop;
        private MethodInfo _method;
        private FieldInfo _field;
        private bool _resolved;
        private bool _attempted;
        private string _resolvedName = "";
        private string _declaringType = "";

        public BoolProbe(string label, params string[] candidates)
        {
            _label = label;
            _candidates = candidates;
        }

        public bool Resolved { get { return _resolved; } }
        public string Label { get { return _label; } }

        /// <summary>
        /// Resolve against the runtime type of <paramref name="sample"/>. Safe to
        /// call every frame - it only does work the first time it sees an
        /// instance, which is the first frame a local player exists.
        /// </summary>
        public void TryResolve(object sample)
        {
            if (_attempted || sample == null) return;
            _attempted = true;

            Type sampleType = sample.GetType();

            // DeclaredOnly, most-derived first - same reason as Member.Bind: a
            // shadowed member makes a whole-hierarchy lookup ambiguous. Here the
            // old catch swallowed that into "candidate not found", which is the
            // silent-failure shape this class exists to avoid (F19).
            const BindingFlags DECLARED =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            foreach (string name in _candidates)
            {
                for (Type t = sampleType; t != null && t != typeof(object); t = t.BaseType)
                {
                    try
                    {
                        PropertyInfo p = t.GetProperty(name, DECLARED);
                        if (p != null && p.PropertyType == typeof(bool) && p.CanRead)
                        {
                            _prop = p; Accept(t, name); return;
                        }

                        MethodInfo m = t.GetMethod(name, DECLARED, null, Type.EmptyTypes, null);
                        if (m != null && m.ReturnType == typeof(bool))
                        {
                            _method = m; Accept(t, name + "()"); return;
                        }

                        FieldInfo f = t.GetField(name, DECLARED);
                        if (f != null && f.FieldType == typeof(bool))
                        {
                            _field = f; Accept(t, name); return;
                        }
                    }
                    catch (AmbiguousMatchException) { }
                }
            }
        }

        private void Accept(Type t, string name)
        {
            _resolved = true;
            _resolvedName = name;
            _declaringType = t.Name;
        }

        /// <summary>Reads false when unresolved or when anything goes wrong.</summary>
        public bool Read(object target)
        {
            if (target == null) return false;
            TryResolve(target);
            if (!_resolved) return false;

            try
            {
                if (_prop != null) return (bool)_prop.GetValue(target, null);
                if (_method != null) return (bool)_method.Invoke(target, null);
                if (_field != null) return (bool)_field.GetValue(target);
            }
            catch
            {
                // A member that resolves but throws is worse than one that is
                // missing, so stop using it rather than throwing every frame.
                _resolved = false;
                _resolvedName = "(threw on read, disabled)";
            }
            return false;
        }

        public string Describe()
        {
            if (!_attempted) return _label + " -> not yet probed";
            if (_resolved) return _label + " -> " + _declaringType + "." + _resolvedName;
            return _label + " -> UNRESOLVED (tried: " + string.Join(", ", _candidates) + ")";
        }
    }
}
