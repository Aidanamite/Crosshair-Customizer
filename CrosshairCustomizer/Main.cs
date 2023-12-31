using HarmonyLib;
using HMLLibrary;
using RaftModLoader;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UI;
using System.Runtime.CompilerServices;
using System.Threading;
using System;
using System.Runtime.InteropServices;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;


namespace CrosshairCustomizer
{
    public class Main : Mod
    {
        Harmony harmony;
        public static Sprite originalLoadingSprite;
        public static (Image.FillMethod, int, bool)? originalLoadingSettings;
        public static (Image.FillMethod, int, bool) settings;
        public static LoadedSprite loadingSprite;
        public static float loadingScale;
        public static (Vector2, Vector2)? originalSize;
        public static float scale = 1;
        public static Dictionary<AimSprite, LoadedSprite> replacements = new Dictionary<AimSprite, LoadedSprite>();
        public static Dictionary<AimSprite, float> scales = new Dictionary<AimSprite, float>();
        public void Start()
        {
            (harmony = new Harmony("com.aidanamite.CrosshairCustomizer")).PatchAll();
            Patch_CanvasHelperInitialize.Postfix();
            Log("Mod has been loaded!");
        }

        public void OnModUnload()
        {
            harmony?.UnpatchAll(harmony.Id);
            ResetCrosshair("loadingImage");
            Patch_SetCrosshairImage.CheckSprite();
            UpdateScale(1);
            Log("Mod has been unloaded!");
        }

        public static void UpdateScale(float? scale = null)
        {
            if (scale != null)
                Main.scale = scale.Value;
            if (!ComponentManager<CanvasHelper>.Value?.removeBlockRadialImage || !ComponentManager<CanvasHelper>.Value?.centerAim)
                return;
            if (originalSize == null)
                originalSize = (ComponentManager<CanvasHelper>.Value.removeBlockRadialImage.rectTransform.sizeDelta, ComponentManager<CanvasHelper>.Value.centerAim.rectTransform.sizeDelta);
            ComponentManager<CanvasHelper>.Value.removeBlockRadialImage.rectTransform.SetSizeDelta(originalSize.Value.Item1 * loadingScale * Main.scale);
            ComponentManager<CanvasHelper>.Value.centerAim.rectTransform.SetSizeDelta(originalSize.Value.Item2 * (scales.TryGetValue(Patch_SetCrosshairImage.lastRequest, out var s) ? s : 1) * Main.scale);
        }

        public static void ResetCrosshair(string name) => UpdateCrosshair(name, null, true);
        public static bool UpdateCrosshair(string name, string path, bool resetOnFail)
        {
            if (name.StartsWith("browse_"))
                name = name.Remove(0, 7);
            {
                if (path == (name == "loadingImage" ? loadingSprite?.Path : replacements.TryGetValue((AimSprite)Enum.Parse(typeof(AimSprite), name.Remove(0, 9)), out var s) ? s?.Path : null))
                    return false;
            }
            LoadedSprite i = null;
            if (!string.IsNullOrEmpty(path))
            {
                i = LoadedSprite.Create(path, out var e);
                if (i == null)
                {
                    Debug.LogError(e);
                    if (!resetOnFail)
                        return false;
                }
            }
            if (name == "loadingImage")
            {
                loadingSprite?.Destroy();
                loadingSprite = i;
                if (ComponentManager<CanvasHelper>.Value && ComponentManager<CanvasHelper>.Value.removeBlockRadialImage)
                {
                    if (!originalLoadingSprite)
                        originalLoadingSprite = ComponentManager<CanvasHelper>.Value.removeBlockRadialImage.sprite;
                    if (loadingSprite == null)
                        ComponentManager<CanvasHelper>.Value.removeBlockRadialImage.sprite = originalLoadingSprite;
                    else
                        ComponentManager<CanvasHelper>.Value.removeBlockRadialImage.sprite = loadingSprite.Sprite;
                }
            }
            else
            {
                var key = (AimSprite)Enum.Parse(typeof(AimSprite), name.Remove(0, 9));
                if (replacements.TryGetValue(key, out var s))
                    s.Destroy();
                if (i == null)
                    replacements.Remove(key);
                else
                    replacements[key] = i;
                Patch_SetCrosshairImage.CheckSprite();
            }
            return true;
        }


        void ExtraSettingsAPI_Load() => ExtraSettingsAPI_SettingsClose();
        void ExtraSettingsAPI_SettingsClose()
        {
            UpdateCrosshair("loadingImage", ExtraSettingsAPI_GetInputValue("loadingImage"), true);
            loadingScale = ExtraSettingsAPI_GetInputValue("scale_loadingImage").ParseScale();
            foreach (AimSprite s in Enum.GetValues(typeof(AimSprite)))
            {
                UpdateCrosshair("crosshair" + s, ExtraSettingsAPI_GetInputValue("crosshair" + s), true);
                scales[s] = ExtraSettingsAPI_GetInputValue("scale_crosshair" + s).ParseScale();
            }
            var p = ExtraSettingsAPI_GetComboboxSelectedItem("loadingFill").Split(' ');
            var method = (Image.FillMethod)Enum.Parse(typeof(Image.FillMethod), p[0]);
            var mode = (Enum.Parse(
                method == Image.FillMethod.Horizontal
                ? typeof(Image.OriginHorizontal)
                : method == Image.FillMethod.Vertical
                ? typeof(Image.OriginVertical)
                : method == Image.FillMethod.Radial90
                ? typeof(Image.Origin90)
                : method == Image.FillMethod.Radial180
                ? typeof(Image.Origin180)
                : typeof(Image.Origin360),
                p[1].Split('-')[0]
                ) as IConvertible).ToInt32(System.Globalization.CultureInfo.InvariantCulture);
            settings = (method, mode, p.Length < 3 || p[2] == "CW");
            UpdateLoading(settings);
            UpdateScale(ExtraSettingsAPI_GetInputValue("scale").ParseScale());
        }

        void ExtraSettingsAPI_ButtonPress(string SettingName)
        {
            if (SettingName == "reset")
                ExtraSettingsAPI_ResetAllSettings();
            else if (SettingName.StartsWith("browse_"))
                StartCoroutine(RequestFile("Select New " + SettingName.Remove(0, 7).CamelToTitle(), x => { if (UpdateCrosshair(SettingName, x, false)) ExtraSettingsAPI_SetInputValue(SettingName.Remove(0, 7), x); }));
        }

        public static void UpdateLoading((Image.FillMethod method, int mode, bool clockwise) settings)
        {
            if (!ComponentManager<CanvasHelper>.Value?.removeBlockRadialImage)
                return;
            if (originalLoadingSettings == null)
                originalLoadingSettings = (ComponentManager<CanvasHelper>.Value.removeBlockRadialImage.fillMethod, ComponentManager<CanvasHelper>.Value.removeBlockRadialImage.fillOrigin, ComponentManager<CanvasHelper>.Value.removeBlockRadialImage.fillClockwise);
            (ComponentManager<CanvasHelper>.Value.removeBlockRadialImage.fillMethod, ComponentManager<CanvasHelper>.Value.removeBlockRadialImage.fillOrigin, ComponentManager<CanvasHelper>.Value.removeBlockRadialImage.fillClockwise) = (settings.method, settings.mode, settings.clockwise);
        }

        IEnumerator RequestFile(string title, Action<string> onComplete)
        {
            string result = null;
            Thread t = null;
            t = new Thread(() =>
            {
                if (!DllTest.OpenFileDialog(title, out result))
                    result = "";
            });
            t.IsBackground = true;
            t.Start();
            while (result == null && t.IsAlive)
                yield return null;
            if (!string.IsNullOrEmpty(result))
                onComplete(result);
            yield break;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        bool ExtraSettingsAPI_GetCheckboxState(string SettingName) => false;

        [MethodImpl(MethodImplOptions.NoInlining)]
        string ExtraSettingsAPI_GetInputValue(string SettingName) => null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        void ExtraSettingsAPI_SetInputValue(string SettingName, string NewValue) { }

        [MethodImpl(MethodImplOptions.NoInlining)]
        string ExtraSettingsAPI_GetKeybindName(string SettingName) => null;

        [MethodImpl(MethodImplOptions.NoInlining)]
        int ExtraSettingsAPI_GetComboboxSelectedIndex(string SettingName) => 0;

        [MethodImpl(MethodImplOptions.NoInlining)]
        string ExtraSettingsAPI_GetComboboxSelectedItem(string SettingName) => "";

        [MethodImpl(MethodImplOptions.NoInlining)]
        void ExtraSettingsAPI_ResetAllSettings() { }
    }

    public class LoadedSprite
    {
        public readonly string Path;
        public readonly Sprite Sprite;
        LoadedSprite(string path, Texture2D texture)
        {
            Path = path;
            Sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        }
        public void Destroy()
        {
            if (!Sprite)
                return;
            if (Sprite.texture)
                Object.Destroy(Sprite.texture);
            Object.Destroy(Sprite);
        }
        public static LoadedSprite Create(string path, out Exception exception)
        {
            Texture2D t = null;
            LoadedSprite l = null;
            try
            {
                t = new Texture2D(0, 0);
                t.LoadImage(File.ReadAllBytes(path));
                l = new LoadedSprite(path, t);
            } catch (Exception e)
            {
                exception = e;
                if (t)
                    Object.Destroy(t);
                if (l != null)
                    l.Destroy();
                return null;
            }
            exception = null;
            return l;
        }
    }

    [HarmonyPatch(typeof(CanvasHelper), "SetAimSprite")]
    static class Patch_SetCrosshairImage
    {
        public static AimSprite lastRequest;
        public static void CheckSprite()
        {
            if (ComponentManager<CanvasHelper>.Value)
                ComponentManager<CanvasHelper>.Value.SetAimSprite(lastRequest);
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            for (int i = code.Count - 1; i >= 0; i--)
                if (code[i].opcode == OpCodes.Ldfld && code[i].operand is FieldInfo f && f.FieldType == typeof(Sprite))
                    code.InsertRange(i + 1, new[] {
                        new CodeInstruction(OpCodes.Ldarg_1),
                        new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(Patch_SetCrosshairImage), nameof(OverrideSprite)))
                    });
            return code;
        }

        static Sprite OverrideSprite(Sprite original, AimSprite request)
        {
            lastRequest = request;
            Main.UpdateScale();
            if (Main.replacements.TryGetValue(request, out var loaded) && loaded != null && loaded.Sprite)
                return loaded.Sprite;
            return original;
        }
    }

    [HarmonyPatch(typeof(CanvasHelper), "Awake")]
    static class Patch_CanvasHelperInitialize
    {
        public static void Postfix()
        {
            Main.originalLoadingSettings = null;
            Main.UpdateLoading(Main.settings);
            Patch_SetCrosshairImage.CheckSprite();
            Main.UpdateScale();
            Main.originalLoadingSprite = ComponentManager<CanvasHelper>.Value.removeBlockRadialImage.sprite;
            if (Main.loadingSprite?.Sprite)
                ComponentManager<CanvasHelper>.Value.removeBlockRadialImage.sprite = Main.loadingSprite.Sprite;
        }
    }

    public static class ExtentionMethods
    {
        public static Texture2D GetReadable(this Texture2D source, GraphicsFormat targetFormat, bool mipChain = true, Rect? copyArea = null, RenderTextureFormat format = RenderTextureFormat.Default, RenderTextureReadWrite readWrite = RenderTextureReadWrite.Default) =>
            source.CopyTo(
                new Texture2D(
                    (int)(copyArea?.width ?? source.width),
                    (int)(copyArea?.height ?? source.height),
                    targetFormat,
                    mipChain ? TextureCreationFlags.MipChain : TextureCreationFlags.None),
                copyArea,
                format,
                readWrite);

        public static Texture2D GetReadable(this Texture2D source, TextureFormat? targetFormat = null, bool mipChain = true, Rect? copyArea = null, RenderTextureFormat format = RenderTextureFormat.Default, RenderTextureReadWrite readWrite = RenderTextureReadWrite.Default) =>
            source.CopyTo(
                new Texture2D(
                    (int)(copyArea?.width ?? source.width),
                    (int)(copyArea?.height ?? source.height),
                    targetFormat ?? TextureFormat.ARGB32,
                    mipChain),
                copyArea,
                format,
                readWrite);

        static Texture2D CopyTo(this Texture2D source, Texture2D texture, Rect? copyArea = null, RenderTextureFormat format = RenderTextureFormat.Default, RenderTextureReadWrite readWrite = RenderTextureReadWrite.Default)
        {
            var temp = RenderTexture.GetTemporary(source.width, source.height, 0, format, readWrite);
            Graphics.Blit(source, temp);
            temp.filterMode = FilterMode.Point;
            var prev = RenderTexture.active;
            RenderTexture.active = temp;
            var area = copyArea ?? new Rect(0, 0, temp.width, temp.height);
            texture.ReadPixels(area, 0, 0);
            texture.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(temp);
            return texture;
        }

        public static string CamelToTitle(this string v)
        {
            if (v == null || v.Length == 0)
                return "";
            var b = new System.Text.StringBuilder();
            b.Append(char.ToUpperInvariant(v[0]));
            for (int i = 1; i < v.Length; i++) {
                if (char.IsUpper(v[i]))
                    b.Append(' ');
                v.Append(v[i]);
            }
            return b.ToString();
        }

        public static void SetSizeDelta(this RectTransform rect, Vector2 size)
        {
            if (rect.sizeDelta != size)
                rect.sizeDelta = size;
        }

        public static float ParseScale(this string value)
        {
            if (float.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var n))
            {
                if (n <= 0)
                    return BitConverter.ToSingle(BitConverter.GetBytes(1), 0);
                return n;
            }
            return 1;
        }
    }

     
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public class OpenFileName
    {
        public int structSize = 0;
        public IntPtr dlgOwner = IntPtr.Zero;
        public IntPtr instance = IntPtr.Zero;
        public string filter = null;
        public string customFilter = null;
        public int maxCustFilter = 0;
        public int filterIndex = 0;
        public string file = null;
        public int maxFile = 0;
        public string fileTitle = null;
        public int maxFileTitle = 0;
        public string initialDir = null;
        public string title = null;
        public int flags = 0;
        public short fileOffset = 0;
        public short fileExtension = 0;
        public string defExt = null;
        public IntPtr custData = IntPtr.Zero;
        public IntPtr hook = IntPtr.Zero;
        public string templateName = null;
        public IntPtr reservedPtr = IntPtr.Zero;
        public int reservedInt = 0;
        public int flagsEx = 0;
    }

    class DllTest
    {
        [DllImport("Comdlg32.dll", SetLastError = true, ThrowOnUnmappableChar = true, CharSet = CharSet.Auto)]
        static extern bool GetOpenFileName([In, Out] OpenFileName ofn);

        public static bool OpenFileDialog(string title, out string filename)
        {
            OpenFileName ofn = new OpenFileName();
            ofn.structSize = Marshal.SizeOf(ofn);
            ofn.filter = "All Files\0*.*\0\0";
            ofn.file = new string(new char[256]);
            ofn.maxFile = ofn.file.Length;
            ofn.fileTitle = new string(new char[64]);
            ofn.maxFileTitle = ofn.fileTitle.Length;
            ofn.initialDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            ofn.title = title;
            ofn.defExt = "PNG";
            ofn.flags = 0x00080000 | 0x00001000 | 0x00000800 | 0x00000008;//OFN_EXPLORER|OFN_FILEMUSTEXIST|OFN_PATHMUSTEXIST|OFN_NOCHANGEDIR
            if (GetOpenFileName(ofn))
            {
                filename = ofn.file;
                return true;
            }
            filename = null;
            return false;
        }
    }

}