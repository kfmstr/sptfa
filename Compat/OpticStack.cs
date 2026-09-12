using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SPTFreeAim.Compat
{
    /// <summary>
    /// Glass effects INSIDE the scope, not across the whole screen.
    ///
    /// The owner, looking at the first working version of the lens glare: "you
    /// are applying it to the entire view, not on the transparent lense
    /// material, perhaps would be most appropriate on the sniper scope view
    /// (since it is view in the view)."
    ///
    /// He is right, and the game is built for it. The scope image is rendered by
    /// its own camera with its own PostProcessing v2 stack:
    ///
    ///     OpticCameraManager.Init:
    ///         GetComponent&lt;PostProcessVolume&gt;()  -> _postProcessVolume
    ///         GetComponent&lt;PostProcessLayer&gt;()   -> _postProcessLayer
    ///     OpticCameraManager.SetSSR:
    ///         _postProcessVolume.profile.TryGetSettings&lt;ScreenSpaceReflections&gt;(...)
    ///
    /// F48's dump showed that profile contains ScreenSpaceReflections and nothing
    /// else, so the effects have to be CREATED and added rather than switched on.
    /// PostProcessProfile carries non-generic AddSettings(Type) /
    /// RemoveSettings(Type) / HasSettings(Type), so that needs no generic-method
    /// reflection - which is the only reason this is forty lines rather than two
    /// hundred.
    ///
    /// This one mechanism covers all three cues from the reference footage (F43):
    ///     Bloom              - the bright hotspot on the glass
    ///     ChromaticAberration- the colour fringing at the edge
    ///     Vignette           - the ring, darkening toward the rim
    ///     LensDistortion     - the round distortion that says the glass is curved
    ///
    /// Anything this ADDS, it removes on release. The profile is the game's.
    /// </summary>
    public static class OpticStack
    {
        public static string Status = "not resolved yet";
        public static bool Ready { get { return ProfileAlive(); } }

        private const string Ns = "UnityEngine.Rendering.PostProcessing.";

        private static object _profile;
        private static MethodInfo _mAdd, _mRemove, _mHas;

        // The main camera's PostProcessLayer, and the volumeLayer mask it had
        // before we narrowed it. See IsolateFromMainCamera.
        private static object _mainLayer;
        private static FieldInfo _fMainVolumeLayer;
        private static object _mainMaskOriginal;
        private static bool _isolated;
        public static string Isolation = "not attempted";

        /// <summary>
        /// Which kind of sight is fitted, and therefore whether any of this can
        /// possibly show. Measured from the game, not guessed. See ReadSightKind.
        /// </summary>
        public static string SightKind = "not read yet";
        public static bool HaveOpticCamera;
        public static string DirtStatus = "-";
        private static object _ocm;

        /// <summary>What the scope actually renders into, and whether we changed it.</summary>
        public static string HdrReport = "not read yet";
        public static bool ScopeIsHdr;
        private static bool _forcedHdr;
        private static int  _hdrAttempts;

        /// <summary>Bloom, read back out of the profile a frame after writing it.</summary>
        public static string BloomVerdict = "-";

        // If we had to move the optic volume's GameObject onto a private layer,
        // this is the object and the layer it came from. Restored on release.
        private static GameObject _movedGo;
        private static int _movedFromLayer = -1;

        /// <summary>
        /// _profile is held as object, which defeats UnityEngine.Object's
        /// overloaded == and makes a DESTROYED profile still read as non-null.
        /// The optic camera is rebuilt when the weapon changes, so without this
        /// the mod would keep writing into a dead profile forever and the scope
        /// would quietly show nothing - which is exactly what "none of the lens
        /// effects worked, I tried both optic and holo sight" looks like.
        /// Casting back to UnityEngine.Object restores the real check.
        /// </summary>
        private static bool ProfileAlive()
        {
            UnityEngine.Object uo = _profile as UnityEngine.Object;
            return uo != null;
        }

        // effect short name -> the settings instance in the profile
        private static readonly Dictionary<string, object> _fx = new Dictionary<string, object>();
        // the ones we created, so release puts the profile back as we found it
        private static readonly List<Type> _added = new List<Type>();

        private static readonly string[] Wanted =
        {
            "Bloom", "ChromaticAberration", "Vignette", "LensDistortion"
        };

        public static bool Resolve()
        {
            if (ProfileAlive()) return true;

            // A dead profile means the optic camera was rebuilt (weapon change).
            // Everything we recorded about it refers to an object that no longer
            // exists; the main camera's mask is separate and stays isolated.
            _profile = null;
            _fx.Clear();
            _added.Clear();
            _hdrAttempts = 0;

            try
            {
                Assembly asmCSharp = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
                if (asmCSharp == null) { Status = "Assembly-CSharp not loaded"; return false; }

                Type camMgr = asmCSharp.GetType("EFT.CameraControl.CameraManager", false);
                PropertyInfo pInst = camMgr == null ? null : camMgr.GetProperty("Instance",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object inst = pInst == null ? null : pInst.GetValue(null, null);
                if (inst == null) { Status = "not in a raid yet"; return false; }

                PropertyInfo pOcm = camMgr.GetProperty("OpticCameraManager",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object ocm = pOcm == null ? null : pOcm.GetValue(inst, null);
                if (ocm == null) { Status = "OpticCameraManager is null"; return false; }

                _ocm = ocm;
                ReadSightKind(ocm);

                FieldInfo fVol = ocm.GetType().GetField("_postProcessVolume",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object vol = fVol == null ? null : fVol.GetValue(ocm);
                if (vol == null) { Status = "optic PostProcessVolume is null"; return false; }

                PropertyInfo pProfile = vol.GetType().GetProperty("profile",
                    BindingFlags.Instance | BindingFlags.Public);
                object profile = pProfile == null ? null : pProfile.GetValue(vol, null);
                if (profile == null) { Status = "optic volume has no profile"; return false; }

                Type tProfile = profile.GetType();
                Type tSettings = FindType(Ns + "PostProcessEffectSettings");
                if (tSettings == null) { Status = "PostProcessEffectSettings type not found"; return false; }

                _mAdd = tProfile.GetMethod("AddSettings", new[] { typeof(Type) });
                _mRemove = tProfile.GetMethod("RemoveSettings", new[] { typeof(Type) });
                _mHas = tProfile.GetMethod("HasSettings", new[] { typeof(Type) });
                if (_mAdd == null) { Status = "PostProcessProfile.AddSettings(Type) not found"; return false; }

                _fx.Clear();
                _added.Clear();

                var report = new List<string>();
                foreach (string name in Wanted)
                {
                    Type t = FindType(Ns + name);
                    if (t == null) { report.Add(name + ":TYPE MISSING"); continue; }

                    bool had = _mHas != null && (bool)_mHas.Invoke(profile, new object[] { t });
                    object settings = had ? GetExisting(profile, t)
                                          : _mAdd.Invoke(profile, new object[] { t });

                    if (settings == null) { report.Add(name + ":ADD FAILED"); continue; }

                    if (!had) _added.Add(t);
                    _fx[name] = settings;
                    report.Add(name + (had ? ":was present" : ":added"));
                }

                if (_fx.Count == 0) { Status = "no effects could be added"; return false; }

                _profile = profile;
                IsolateFromMainCamera(asmCSharp, camMgr, inst, vol);
                Status = "driving the optic's own stack";
                Plugin.Log.LogInfo("Optic glass: " + Status + " - " + string.Join(", ", report.ToArray()));
                return true;
            }
            catch (Exception e)
            {
                Status = e.GetType().Name + ": " + e.Message;
                Plugin.Log.LogWarning("Optic glass: " + Status);
                return false;
            }
        }

        /// <summary>
        /// Stop the FPS camera from applying the scope's volume.
        ///
        /// This is the whole reason the first two attempts leaked. In
        /// PostProcessing v2 a volume is not owned by a camera: every
        /// PostProcessLayer applies every GLOBAL volume whose GameObject layer
        /// falls inside that layer's volumeLayer mask. The measured setup:
        ///
        ///     OPTIC volume  BaseOpticCamera(Clone)  layer 8 (Player)  isGlobal
        ///     MAIN  layer   FPS Camera              volumeLayer = -1 (EVERYTHING)
        ///
        /// Everything includes layer 8, so the FPS camera applies the scope's
        /// volume as well, and effects added "to the scope" arrive on the whole
        /// screen. F52.
        ///
        /// The fix is one bit: clear the optic volume's layer from the MAIN
        /// layer's mask. The main camera keeps its own volume (layer 0) and every
        /// other layer it had; it simply stops reading the one that belongs to the
        /// scope. Restored exactly on release.
        ///
        /// Narrowing the OPTIC layer's mask instead would not help - the leak is
        /// on the main camera's side, and the optic camera reading Everything is
        /// harmless.
        ///
        /// One caveat, and the reason for the census below: layer 8 is Player,
        /// and any OTHER global volume parked there would be dropped from the
        /// main view too. So we count what else lives on that layer first. If the
        /// scope has company, we move the scope's own GameObject onto an unused
        /// layer and drop THAT bit instead, which cannot cost anything because
        /// nothing else is on it. Only if there is no free layer do we drop the
        /// shared one, and then the log names exactly what went with it.
        /// </summary>
        private static void IsolateFromMainCamera(Assembly asmCSharp, Type camMgr, object inst, object opticVolume)
        {
            try
            {
                var opticGo = GetMember(opticVolume, "gameObject") as GameObject;
                if (opticGo == null) { Isolation = "optic volume has no GameObject"; return; }
                int opticLayer = opticGo.layer;

                // Who else is on this layer, and which layers are free?
                int occupied;
                List<string> company = Census(opticVolume, opticLayer, out occupied);

                if (company.Count > 0)
                {
                    int free = FreeLayer(occupied);
                    if (free >= 0)
                    {
                        Plugin.Log.LogInfo(string.Format(
                            "Optic glass: layer {0} also carries {1} - moving the scope volume to unused layer {2} instead of dropping a shared layer.",
                            opticLayer, string.Join(", ", company.ToArray()), free));
                        _movedGo = opticGo;
                        _movedFromLayer = opticLayer;
                        opticGo.layer = free;
                        opticLayer = free;
                    }
                    else
                    {
                        Plugin.Log.LogWarning(string.Format(
                            "Optic glass: no unused layer available, so dropping layer {0} from the main camera also drops {1} from the main view.",
                            opticLayer, string.Join(", ", company.ToArray())));
                    }
                }

                FieldInfo fMainLayer = camMgr.GetField("_postProcessLayer",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object mainLayer = fMainLayer == null ? null : fMainLayer.GetValue(inst);
                if (mainLayer == null) { Isolation = "main PostProcessLayer is null"; return; }

                FieldInfo fVolumeLayer = mainLayer.GetType().GetField("volumeLayer",
                    BindingFlags.Instance | BindingFlags.Public);
                if (fVolumeLayer == null) { Isolation = "PostProcessLayer.volumeLayer not found"; return; }

                object maskBoxed = fVolumeLayer.GetValue(mainLayer);
                PropertyInfo pValue = maskBoxed == null ? null
                    : maskBoxed.GetType().GetProperty("value", BindingFlags.Instance | BindingFlags.Public);
                if (pValue == null) { Isolation = "LayerMask.value not found"; return; }

                int before = (int)pValue.GetValue(maskBoxed, null);
                int after = before & ~(1 << opticLayer);

                if (after == before)
                {
                    // Nothing to do - and if we moved the object to get here, put
                    // it back, because the move bought us nothing.
                    RestoreMovedLayer();
                    Isolation = string.Format("already isolated (main mask {0} excludes layer {1})",
                        before, opticLayer);
                    Plugin.Log.LogInfo("Optic glass: " + Isolation);
                    return;
                }

                _mainLayer = mainLayer;
                _fMainVolumeLayer = fVolumeLayer;
                _mainMaskOriginal = maskBoxed;

                pValue.SetValue(maskBoxed, after, null);
                fVolumeLayer.SetValue(mainLayer, maskBoxed);   // LayerMask is a struct - write it back
                _isolated = true;

                Isolation = string.Format("main mask {0} -> {1} (dropped layer {2}{3})",
                    before, after, opticLayer,
                    _movedGo == null ? "" : ", scope moved off " + _movedFromLayer);
                Plugin.Log.LogInfo("Optic glass: " + Isolation +
                    ". The FPS camera no longer applies the scope's volume.");
            }
            catch (Exception e)
            {
                Isolation = e.GetType().Name + ": " + e.Message;
                Plugin.Log.LogWarning("Optic glass: could not isolate - " + Isolation);
            }
        }

        private static void RestoreMainCameraMask()
        {
            RestoreMovedLayer();

            if (!_isolated || _mainLayer == null || _fMainVolumeLayer == null) return;
            try { _fMainVolumeLayer.SetValue(_mainLayer, _mainMaskOriginal); }
            catch { }
            _isolated = false;
            _mainLayer = null;
            _fMainVolumeLayer = null;
            Isolation = "restored";
        }

        private static void RestoreMovedLayer()
        {
            if (_movedGo == null || _movedFromLayer < 0) { _movedGo = null; _movedFromLayer = -1; return; }
            try { _movedGo.layer = _movedFromLayer; }
            catch { }
            _movedGo = null;
            _movedFromLayer = -1;
        }

        /// <summary>
        /// Every other PostProcessVolume that would be collateral damage if we
        /// dropped <paramref name="layer"/> from the main camera, plus the set of
        /// layers that are spoken for at all.
        ///
        /// Only GLOBAL volumes count as collateral. A local volume is bounded by
        /// its collider and only affects the camera when the camera is inside it,
        /// so losing it from the main mask is not the same kind of loss - but it
        /// still occupies its layer, so it goes into the occupied set.
        /// </summary>
        private static List<string> Census(object opticVolume, int layer, out int occupied)
        {
            var company = new List<string>();
            occupied = 0;

            // Named layers are the game's; treat them as taken even when empty,
            // so we never squat on something Tarkov means to use later.
            for (int i = 0; i < 32; i++)
                if (!string.IsNullOrEmpty(LayerMask.LayerToName(i))) occupied |= 1 << i;

            Type tVolume = FindType(Ns + "PostProcessVolume");
            if (tVolume == null) return company;

            UnityEngine.Object[] all;
            try { all = UnityEngine.Object.FindObjectsOfType(tVolume); }
            catch { return company; }

            foreach (UnityEngine.Object o in all)
            {
                if (o == null || ReferenceEquals(o, opticVolume)) continue;

                var go = GetMember(o, "gameObject") as GameObject;
                if (go == null) continue;
                occupied |= 1 << go.layer;

                if (go.layer != layer) continue;
                object isGlobal = GetMember(o, "isGlobal");
                if (isGlobal is bool && !(bool)isGlobal) continue;
                company.Add(go.name);
            }
            return company;
        }

        /// <summary>Lowest user layer that is unnamed and carries no volume, or -1.</summary>
        private static int FreeLayer(int occupied)
        {
            for (int i = 8; i < 32; i++)
                if ((occupied & (1 << i)) == 0) return i;
            return -1;
        }

        /// <summary>
        /// Say out loud whether a scope image exists at all.
        ///
        /// The optic camera is turned on by OpticSight and only by OpticSight:
        ///
        ///     OpticCameraManager._currentOpticSight : OpticSight
        ///     OpticCameraManager.OnOpticSightEnabled / OnOpticSightDisabled
        ///
        /// A holo or red dot is a different class entirely -
        ///
        ///     CollimatorSight : MonoBehaviour
        ///         MeshRenderer CollimatorMeshRenderer
        ///         Material     CollimatorMaterial
        ///
        /// - a mesh with a material on it, and nothing else. No camera, no render
        /// texture, no PostProcessVolume. So on a red dot there is no second
        /// render of the world to post-process: what you see "through" the glass
        /// is just the main view, unchanged, with a reticle drawn on top.
        ///
        /// That means every glass effect in this file needs a MAGNIFIED optic to
        /// have anywhere to land, and saying so on the HUD is the difference
        /// between a dial that is off and a dial that cannot apply. F54.
        /// </summary>
        private static void ReadSightKind(object ocm)
        {
            try
            {
                UnityEngine.Object current = GetMember(ocm, "CurrentOpticSight") as UnityEngine.Object;
                object rendering = GetMember(ocm, "IsAnyOpticCameraRendering");
                bool live = rendering is bool && (bool)rendering;

                HaveOpticCamera = current != null;
                SightKind = HaveOpticCamera
                    ? (live ? "magnified optic, tube rendering" : "magnified optic, tube idle")
                    : "NO scope image - a holo/red dot has no optic camera";
            }
            catch (Exception e)
            {
                SightKind = "unreadable: " + e.GetType().Name;
                HaveOpticCamera = false;
            }
        }

        /// <summary>
        /// Bloom needs numbers brighter than white, and the scope may not have any.
        ///
        /// OpticCameraManager.SetResolution, decompiled:
        ///
        ///     RenderTextureFormat fmt = Camera.allowHDR ? ARGBHalf : ARGB32;
        ///     _renderTexture = new RenderTexture(res, res, 24, fmt, Default);
        ///     ...
        ///     Camera.targetTexture = _renderTexture;
        ///     Shader.SetGlobalTexture(_camTexId, _renderTexture);
        ///
        /// So the scope image is ARGB32 unless the optic camera allows HDR, and
        /// ARGB32 clamps every pixel at 1.0. In that buffer a lamp and a white
        /// wall are the SAME VALUE, which is why "make the lamp brighter" had
        /// nowhere to go, and why a bloom threshold above 1.0 selects literally
        /// nothing. That is F56, and it is the whole story of "glare didn't work"
        /// while distortion and rim darkening were fine: those two are screen-space
        /// operations that do not care how bright anything is.
        ///
        /// The fix is to set allowHDR and let the GAME rebuild the texture. Not to
        /// build one here: SetResolution also rebinds the global shader texture the
        /// scope lens samples, and a hand-rolled swap would leave the lens reading a
        /// texture nobody renders into. Use the game's method or do not do it.
        /// </summary>
        private static void EnsureHdr(bool want)
        {
            try
            {
                Camera cam = GetMember(_ocm, "Camera") as Camera;
                if (cam == null) { HdrReport = "optic camera not reachable"; return; }

                RenderTexture rt = GetMember(_ocm, "_renderTexture") as RenderTexture;
                ScopeIsHdr = cam.allowHDR;

                string fmt = rt == null ? "no texture" : rt.format.ToString();

                if (want && !cam.allowHDR && _hdrAttempts < 3)
                {
                    // Capped. SetResolution destroys and recreates the render
                    // texture, so if something in the game sets allowHDR back
                    // every frame, an uncapped retry would rebuild the scope's
                    // texture sixty times a second. Three tries, then report and
                    // stop rather than stutter forever.
                    _hdrAttempts++;

                    object resBoxed = GetMember(_ocm, "OpticFinalResolution");
                    MethodInfo setRes = _ocm.GetType().GetMethod("SetResolution",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (setRes == null || !(resBoxed is int))
                    {
                        HdrReport = "LDR (" + fmt + ") - SetResolution not reachable, cannot fix";
                        return;
                    }

                    cam.allowHDR = true;
                    setRes.Invoke(_ocm, new object[] { (int)resBoxed });
                    _forcedHdr = true;
                    ScopeIsHdr = true;

                    RenderTexture after = GetMember(_ocm, "_renderTexture") as RenderTexture;
                    string nowFmt = after == null ? "no texture" : after.format.ToString();

                    HdrReport = "HDR forced on (" + fmt + " -> " + nowFmt + ")";
                    Plugin.Log.LogInfo("Optic glass: scope was LDR " + fmt +
                        ", forced allowHDR and rebuilt through the game's SetResolution -> " + nowFmt +
                        ". Bloom can now see values above white.");
                    return;
                }

                HdrReport = (cam.allowHDR ? "HDR" : "LDR") + " (" + fmt + ")" +
                            (_forcedHdr ? " forced" : "") +
                            (want && !cam.allowHDR && _hdrAttempts >= 3
                                ? " <color=#ff6666>could not force</color>" : "");
            }
            catch (Exception e)
            {
                HdrReport = "unreadable: " + e.GetType().Name;
            }
        }

        private static void RestoreHdr()
        {
            if (!_forcedHdr) return;
            _forcedHdr = false;
            try
            {
                Camera cam = GetMember(_ocm, "Camera") as Camera;
                object resBoxed = GetMember(_ocm, "OpticFinalResolution");
                MethodInfo setRes = _ocm == null ? null : _ocm.GetType().GetMethod("SetResolution",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (cam == null || setRes == null || !(resBoxed is int)) return;

                cam.allowHDR = false;
                setRes.Invoke(_ocm, new object[] { (int)resBoxed });
                HdrReport = "restored to LDR";
            }
            catch { }
        }

        private static object GetMember(object target, string name)
        {
            if (target == null) return null;
            Type t = target.GetType();
            PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) { try { return p.GetValue(target, null); } catch { } }
            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null) { try { return f.GetValue(target); } catch { } }
            return null;
        }

        private static object GetExisting(object profile, Type effectType)
        {
            FieldInfo fList = profile.GetType().GetField("settings",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var list = fList == null ? null : fList.GetValue(profile) as System.Collections.IEnumerable;
            if (list == null) return null;
            foreach (object s in list)
                if (s != null && effectType.IsInstanceOfType(s)) return s;
            return null;
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = null;
                try { t = a.GetType(fullName, false); } catch { }
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>
        /// A ParameterOverride only participates when overrideState is true.
        /// Writing .value alone leaves the effect reading the profile default and
        /// doing nothing visible - F34's rule, and the one that would silently
        /// waste a whole round here.
        /// </summary>
        private static void SetParam(object settings, string field, object value)
        {
            if (settings == null) return;
            FieldInfo f = settings.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.Public);
            object param = f == null ? null : f.GetValue(settings);
            if (param == null) return;

            Type tp = param.GetType();
            FieldInfo fv = tp.GetField("value", BindingFlags.Instance | BindingFlags.Public);
            FieldInfo fo = tp.GetField("overrideState", BindingFlags.Instance | BindingFlags.Public);
            if (fv != null) fv.SetValue(param, value);
            if (fo != null) fo.SetValue(param, true);
        }

        /// <summary>
        /// Write, then read back, and say which of the three worlds we are in.
        /// F34's rule: a call that succeeds is not a call that did something.
        ///
        /// It also says whether the threshold can select anything at all, because
        /// in an LDR scope buffer a threshold above 1.0 is arithmetically dead and
        /// looks exactly like a broken effect.
        /// </summary>
        private static void VerifyBloom(object bloomSettings, float wanted)
        {
            try
            {
                FieldInfo f = bloomSettings.GetType().GetField("intensity",
                    BindingFlags.Instance | BindingFlags.Public);
                object param = f == null ? null : f.GetValue(bloomSettings);
                FieldInfo fv = param == null ? null
                    : param.GetType().GetField("value", BindingFlags.Instance | BindingFlags.Public);
                object now = fv == null ? null : fv.GetValue(param);

                if (!(now is float)) { BloomVerdict = "unreadable"; return; }
                float actual = (float)now;

                if (Mathf.Abs(actual - wanted) > 0.01f)
                    BloomVerdict = string.Format("OVERWRITTEN wrote {0:F1} read {1:F1}", wanted, actual);
                else if (!ScopeIsHdr && _lastThreshold > 1f)
                    BloomVerdict = string.Format("DEAD: threshold {0:F1} > 1.0 in an LDR scope", _lastThreshold);
                else
                    BloomVerdict = string.Format("live {0:F1}", actual);
            }
            catch { BloomVerdict = "unreadable"; }
        }

        private static float _lastThreshold;

        /// <summary>One short live/dead token per effect, for the HUD.</summary>
        public static string FxVerdict = "-";

        /// <summary>
        /// What the engine will actually use, read back out of the parameter we
        /// just wrote. Three of the four effects were fine and one was writing a
        /// number twenty times too small; nothing on screen could tell those
        /// apart, and nothing in the code was reporting either. F58.
        /// </summary>
        private static string Readback(string fx, string field, float wanted)
        {
            string tag = fx.Substring(0, 2).ToLower();
            object s;
            if (!_fx.TryGetValue(fx, out s) || s == null) return tag + ":absent";
            try
            {
                FieldInfo f = s.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public);
                object param = f == null ? null : f.GetValue(s);
                FieldInfo fv = param == null ? null
                    : param.GetType().GetField("value", BindingFlags.Instance | BindingFlags.Public);
                object now = fv == null ? null : fv.GetValue(param);
                if (!(now is float)) return tag + ":?";

                float actual = (float)now;
                if (Mathf.Abs(wanted) <= 0.001f) return tag + ":off";
                if (Mathf.Abs(actual - wanted) > 0.01f)
                    return string.Format("{0}:<color=#ff6666>lost {1:F2}</color>", tag, actual);
                return string.Format("{0}:{1:F2}", tag, actual);
            }
            catch { return tag + ":?"; }
        }

        private static void Enable(string name, bool on)
        {
            object s;
            if (!_fx.TryGetValue(name, out s) || s == null) return;

            SetParam(s, "enabled", on);
            FieldInfo fActive = s.GetType().GetField("active",
                BindingFlags.Instance | BindingFlags.Public);
            if (fActive != null) fActive.SetValue(s, on);
        }

        /// <summary>
        /// The dials, as a named bundle rather than seven positional floats.
        ///
        /// Seven bare floats in a row is a transposition waiting to happen, and it
        /// would be invisible: swap fringe and vignette and the code compiles, the
        /// build is green, the effects all still "work", and the only symptom is
        /// two dials that do each other's job. Field names at the call site cost
        /// nothing and make that impossible.
        /// </summary>
        public struct Glass
        {
            public float Bloom;
            public float BloomThreshold;
            public float BloomSpread;
            public float Dirt;
            public float Fringe;
            public float Vignette;
            public float Distortion;
            public bool  ForceHdr;
        }

        public static void Drive(Glass g)
        {
            float bloom = g.Bloom, dirt = g.Dirt, fringe = g.Fringe;
            float vignette = g.Vignette, distortion = g.Distortion;

            if (!ProfileAlive())
            {
                // The optic camera was rebuilt under us. Drop the stale handle so
                // the next frame re-resolves against the new one.
                _profile = null;
                Status = "optic camera rebuilt - re-resolving";
                return;
            }
            // Re-read every frame: the fitted sight changes without the profile
            // changing, so a value latched at resolve time would go stale the
            // first time a scope came off. F48's dump had exactly that bug.
            if (_ocm != null) ReadSightKind(_ocm);

            try
            {
                object s;

                bool bloomOn = bloom > 0.001f;
                Enable("Bloom", bloomOn);
                if (bloomOn && _fx.TryGetValue("Bloom", out s))
                {
                    SetParam(s, "intensity", bloom);

                    // Threshold and spread used to be hardcoded 0.9 and 7, which
                    // made "brighter" mean only "turn intensity up", and turning
                    // intensity up past a point hazes the whole tube instead of
                    // making the LAMP brighter. They are the two dials that
                    // separate a hot light source from the rest of the image, so
                    // they belong in the owner's hands, not in this file.
                    SetParam(s, "threshold", g.BloomThreshold);
                    _lastThreshold = g.BloomThreshold;
                    SetParam(s, "diffusion", Mathf.Clamp(g.BloomSpread, 1f, 10f));

                    // A hard-ish knee, so the threshold actually selects rather
                    // than fading everything in gradually around it. With a soft
                    // knee the "only the lamp blooms" behaviour never arrives.
                    SetParam(s, "softKnee", 0.2f);

                    // PPv2's own default, set explicitly. clamp caps how much a
                    // single pixel may contribute; left to whatever the profile
                    // carries, a low value would silently put a ceiling on
                    // exactly the bright-light case this is for.
                    SetParam(s, "clamp", 65472f);

                    // Only when bloom is actually asked for: a render-target
                    // format change is not something to do to someone who never
                    // turned this on.
                    EnsureHdr(g.ForceHdr);
                    VerifyBloom(s, bloom);

                    // Dirt is what turns a glow into GLASS. It rides on bloom -
                    // PPv2 modulates the bloom buffer by the dirt texture - so a
                    // dirt dial with the bloom dial at zero is arithmetically
                    // incapable of showing anything, the same trap as F48. That
                    // is stated in the config description rather than left to be
                    // discovered in a raid.
                    Texture tex = GameRefs.PrismDirtTexture();
                    bool dirtOn = dirt > 0.001f && tex != null;
                    if (dirtOn) SetParam(s, "dirtTexture", tex);
                    SetParam(s, "dirtIntensity", dirtOn ? dirt : 0f);
                    DirtStatus = tex == null ? "no dirt texture" : (dirtOn ? "dirt on" : "dirt off");
                }

                bool fringeOn = fringe > 0.001f;
                Enable("ChromaticAberration", fringeOn);
                if (fringeOn && _fx.TryGetValue("ChromaticAberration", out s))
                {
                    // ChromaticAberrationRenderer.Render, from IL:
                    //
                    //     _ChromaticAberration_Amount = intensity * 0.05f
                    //
                    // The engine divides the dial by twenty before the shader ever
                    // sees it, so a "full strength" 1.0 reaches the shader as 0.05
                    // and the owner's 0.30 as 0.015 - a fraction of a pixel at
                    // scope resolution, which is indistinguishable from the effect
                    // being broken. It was the only one of the four whose internal
                    // scaling I never checked, and the only one that looked dead.
                    //
                    // Scaled here, exactly as LensDistortion already is, so the
                    // dial means what it says: 1.0 is the strongest the shader can
                    // draw. F58.
                    SetParam(s, "intensity", fringe * 20f);

                    // fastMode picks CHROMATIC_ABERRATION_LOW, a cheaper and much
                    // weaker path. Written explicitly so a profile default cannot
                    // quietly halve the effect.
                    SetParam(s, "fastMode", false);
                }

                bool vigOn = vignette > 0.001f;
                Enable("Vignette", vigOn);
                if (vigOn && _fx.TryGetValue("Vignette", out s))
                {
                    SetParam(s, "intensity", vignette);
                    SetParam(s, "smoothness", 0.6f);
                    SetParam(s, "roundness", 1f);
                    SetParam(s, "rounded", true);
                }

                bool distOn = Mathf.Abs(distortion) > 0.001f;
                Enable("LensDistortion", distOn);
                if (distOn && _fx.TryGetValue("LensDistortion", out s))
                {
                    // PPv2 LensDistortion intensity is -100..100. The config dial is
                    // -1..1 because nobody thinks in hundreds; scaled here so the
                    // number in the menu stays something a person can picture.
                    SetParam(s, "intensity", distortion * 100f);
                    SetParam(s, "scale", 1f);
                }

                FxVerdict = Readback("Bloom", "intensity", bloom)
                          + "  " + Readback("ChromaticAberration", "intensity", fringe * 20f)
                          + "  " + Readback("Vignette", "intensity", vignette)
                          + "  " + Readback("LensDistortion", "intensity", distortion * 100f);

                MarkDirty();
            }
            catch (Exception e)
            {
                Status = e.GetType().Name + " while writing - giving up";
                Plugin.Log.LogWarning("Optic glass: " + Status);
                _profile = null;
            }
        }

        private static void MarkDirty()
        {
            FieldInfo f = _profile.GetType().GetField("isDirty",
                BindingFlags.Instance | BindingFlags.Public);
            if (f != null) f.SetValue(_profile, true);
        }

        /// <summary>Put the profile back the way it was found. Never throws.</summary>
        public static void Release()
        {
            // The mask edit outlives the profile: the optic camera can be torn
            // down and rebuilt while the main camera's PostProcessLayer stays put,
            // so releasing has to run even when there is nothing left to undo on
            // the profile itself. Otherwise the mod would leave the main camera
            // permanently blind to a layer.
            if (ProfileAlive())
            {
                try
                {
                    foreach (string name in Wanted) Enable(name, false);

                    if (_mRemove != null)
                        foreach (Type t in _added) _mRemove.Invoke(_profile, new object[] { t });

                    MarkDirty();
                }
                catch { }
            }

            RestoreHdr();
            RestoreMainCameraMask();

            _added.Clear();
            _fx.Clear();
            _profile = null;
            Status = "released";
        }
    }
}
