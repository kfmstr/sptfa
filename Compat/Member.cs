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
            const BindingFlags ANY = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (owner == null) return false;

            foreach (string n in _names)
            {
                PropertyInfo p = owner.GetProperty(n, ANY);
                if (p != null) { _prop = p; _resolvedName = n; _kind = "property"; _declaring = owner.Name; return true; }

                FieldInfo f = owner.GetField(n, ANY);
                if (f != null) { _field = f; _resolvedName = n; _kind = "field"; _declaring = owner.Name; return true; }
            }
            return false;
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
