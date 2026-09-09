using System;
using System.Reflection;

namespace SPTFreeAim.Compat
{
    /// <summary>
    /// A game member that might be a property or a field, resolved by name.
    ///
    /// This distinction is not cosmetic. Both reference mods write
    /// <c>pwa.HandsContainer.WeaponRootAnim</c>, which reads like a property
    /// chain, and it is natural to reach for <c>GetProperty</c>. On SPT 4.1.5
    /// every one of those is a public FIELD, and a property-only lookup returns
    /// null for all of them - so the mod resolves "successfully", does nothing,
    /// and gives you no clue why.
    ///
    /// Checking both, and saying which one landed, costs nothing and removes a
    /// whole category of silent failure.
    /// </summary>
    public class Member
    {
        private readonly string _label;
        private readonly string[] _names;

        private PropertyInfo _prop;
        private FieldInfo _field;
        private string _resolvedName = "";
        private string _kind = "";
        private string _declaring = "";

        public Member(string label, params string[] names)
        {
            _label = label;
            _names = names;
        }

        public bool Resolved { get { return _prop != null || _field != null; } }
        public string Label { get { return _label; } }

        public Type MemberType
        {
            get
            {
                if (_prop != null) return _prop.PropertyType;
                if (_field != null) return _field.FieldType;
                return null;
            }
        }

        /// <summary>True when the member can be assigned to.</summary>
        public bool Writable
        {
            get
            {
                if (_field != null) return !_field.IsInitOnly && !_field.IsLiteral;
                if (_prop != null) return _prop.CanWrite && _prop.GetSetMethod(true) != null;
                return false;
            }
        }

        public bool Bind(Type owner)
        {
            if (owner == null) return false;

            // Binding is repeatable: the same Member gets pointed at a new type
            // when the game hands us a different concrete class. Clear first, so a
            // rebind that finds nothing cannot leave the previous type's
            // PropertyInfo in place and then throw on the wrong target.
            _prop = null;
            _field = null;
            _resolvedName = "";
            _kind = "";
            _declaring = "";

            // Walk the hierarchy one level at a time with DeclaredOnly rather than
            // letting reflection search it in one go.
            //
            // A derived type can redeclare a member its base already has, with a
            // NARROWER TYPE. EFT.Player.FirearmController declares
            //     Weapon Item
            // over AbstractHandsController's
            //     Item Item
            // - same name, different return type, two separate vtable slots. A
            // whole-hierarchy GetProperty cannot choose between them and throws
            // AmbiguousMatchException, which is how this took the mod down
            // mid-raid (docs/07-FINDINGS.md F19).
            //
            // Walking most-derived-first resolves it the way C# itself would: the
            // closest declaration wins.
            const BindingFlags DECLARED =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            foreach (string n in _names)
            {
                for (Type t = owner; t != null && t != typeof(object); t = t.BaseType)
                {
                    try
                    {
                        PropertyInfo p = t.GetProperty(n, DECLARED);
                        if (p != null) { _prop = p; Accept(n, "property", t); return true; }

                        FieldInfo f = t.GetField(n, DECLARED);
                        if (f != null) { _field = f; Accept(n, "field", t); return true; }
                    }
                    catch (AmbiguousMatchException)
                    {
                        // Two declarations on the SAME type - vanishingly rare, and
                        // nothing sensible to pick. Skip the level rather than die.
                    }
                }
            }
            return false;
        }

        private void Accept(string name, string kind, Type declaring)
        {
            _resolvedName = name;
            _kind = kind;
            _declaring = declaring.Name;
        }

        public object Get(object target)
        {
            if (target == null) return null;
            try
            {
                if (_prop != null) return _prop.GetValue(target, null);
                if (_field != null) return _field.GetValue(target);
            }
            catch { }
            return null;
        }

        public bool Set(object target, object value)
        {
            if (target == null || !Writable) return false;
            try
            {
                if (_prop != null) { _prop.SetValue(target, value, null); return true; }
                if (_field != null) { _field.SetValue(target, value); return true; }
            }
            catch { }
            return false;
        }

        public T Get<T>(object target, T fallback = default(T))
        {
            object v = Get(target);
            return v is T ? (T)v : fallback;
        }

        public string Describe()
        {
            if (!Resolved) return _label + " -> MISSING (tried: " + string.Join(", ", _names) + ")";
            return _label + " -> " + _declaring + "." + _resolvedName + " (" + _kind +
                   (Writable ? ", writable" : ", read-only") + ")";
        }
    }
}
