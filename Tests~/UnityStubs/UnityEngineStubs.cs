// Bouchons de l'API Unity utilisée par le SDK. Ils servent UNIQUEMENT à vérifier, hors de Unity,
// que Runtime/Unity et Samples~ compilent (`dotnet build`). Ils ne font rien et ne sont pas livrés.
#pragma warning disable CS0067, CS0649, CS0660, CS0661
using System;
using System.Collections;

namespace UnityEngine
{
    public class Object
    {
        public HideFlags hideFlags;
        public static void Destroy(Object target) { }
        public static void DontDestroyOnLoad(Object target) { }
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !ReferenceEquals(a, b);
    }

    public class Component : Object
    {
        public GameObject gameObject;
    }

    public class GameObject : Object
    {
        public T AddComponent<T>() where T : Component, new() => new T();
    }

    public class MonoBehaviour : Component
    {
        public Coroutine StartCoroutine(IEnumerator routine) => null;
    }

    public class Coroutine { }
    public class WaitForSecondsRealtime { public WaitForSecondsRealtime(float seconds) { } }
    public class AsyncOperation { public event Action<AsyncOperation> completed; }

    [AttributeUsage(AttributeTargets.Class)] public sealed class AddComponentMenu : Attribute { public AddComponentMenu(string name) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string text) { } }

    public enum HideFlags { None, HideAndDontSave }
    public enum FontStyle { Normal, Bold }
    public enum TextAnchor { MiddleLeft, MiddleCenter }

    public struct Color
    {
        public Color(float r, float g, float b, float a) { }
        public static Color white => default;
    }

    public struct Color32
    {
        public Color32(byte r, byte g, byte b, byte a) { }
        public static implicit operator Color(Color32 c) => default;
    }

    public struct Vector3 { public Vector3(float x, float y, float z) { } }
    public struct Matrix4x4 { public static Matrix4x4 Scale(Vector3 v) => default; }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
    }

    public class RectOffset { public RectOffset(int left, int right, int top, int bottom) { } }

    public class Texture2D : Object
    {
        public Texture2D(int width, int height) { }
        public void SetPixel(int x, int y, Color color) { }
        public void Apply() { }
    }

    public class GUIStyleState { public Texture2D background; public Color textColor; }

    public class GUIStyle
    {
        public GUIStyle(GUIStyle other) { }
        public int fontSize;
        public FontStyle fontStyle;
        public bool wordWrap;
        public TextAnchor alignment;
        public RectOffset padding, margin;
        public GUIStyleState normal = new GUIStyleState(), hover = new GUIStyleState(), active = new GUIStyleState(), focused = new GUIStyleState();
    }

    public class GUISkin { public GUIStyle label, button, textField; }

    public static class GUI
    {
        public static int depth;
        public static Matrix4x4 matrix;
        public static Color color;
        public static bool enabled;
        public static GUISkin skin;
        public static void DrawTexture(Rect position, Texture2D image) { }
    }

    public class GUILayoutOption { }

    public static class GUILayout
    {
        public static void BeginArea(Rect area) { }
        public static void EndArea() { }
        public static void BeginHorizontal() { }
        public static void EndHorizontal() { }
        public static void BeginVertical() { }
        public static void EndVertical() { }
        public static void Space(float pixels) { }
        public static void FlexibleSpace() { }
        public static void Label(string text, params GUILayoutOption[] options) { }
        public static void Label(string text, GUIStyle style, params GUILayoutOption[] options) { }
        public static bool Button(string text, params GUILayoutOption[] options) => false;
        public static bool Button(string text, GUIStyle style, params GUILayoutOption[] options) => false;
        public static string TextField(string text, int maxLength, GUIStyle style, params GUILayoutOption[] options) => text;
        public static GUILayoutOption Width(float width) => null;
        public static GUILayoutOption Height(float height) => null;
    }

    public static class Screen { public static int width, height; }
    public static class Application { public static void OpenURL(string url) { } }
    public static class Debug { public static void LogException(Exception exception) { } }

    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);
    }
}

namespace UnityEngine.Networking
{
    public class DownloadHandler { public string text; }
    public class DownloadHandlerBuffer : DownloadHandler { }
    public class UploadHandler { }
    public class UploadHandlerRaw : UploadHandler { public UploadHandlerRaw(byte[] data) { } }
    public class UnityWebRequestAsyncOperation : AsyncOperation { }

    public class UnityWebRequest : IDisposable
    {
        public enum Result { InProgress, Success, ConnectionError, ProtocolError, DataProcessingError }

        public UnityWebRequest(string url, string method) { }
        public DownloadHandler downloadHandler;
        public UploadHandler uploadHandler;
        public int timeout;
        public Result result;
        public string error;
        public long responseCode;
        public void SetRequestHeader(string name, string value) { }
        public string GetResponseHeader(string name) => null;
        public UnityWebRequestAsyncOperation SendWebRequest() => new UnityWebRequestAsyncOperation();
        public void Dispose() { }
    }
}
