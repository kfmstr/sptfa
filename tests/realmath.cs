// Real implementations of the Unity maths the drive loop uses, so the loop can
// be exercised against docs/01-SPEC.md outside the game.
using System;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float magnitude { get { return (float)Math.Sqrt(x * x + y * y); } }
        public static Vector2 zero { get { return new Vector2(0, 0); } }
        public static Vector2 operator +(Vector2 a, Vector2 b) { return new Vector2(a.x + b.x, a.y + b.y); }
        public static Vector2 operator -(Vector2 a, Vector2 b) { return new Vector2(a.x - b.x, a.y - b.y); }
        public static Vector2 operator -(Vector2 a) { return new Vector2(-a.x, -a.y); }
        public static Vector2 operator *(Vector2 a, float f) { return new Vector2(a.x * f, a.y * f); }
        public static Vector2 ClampMagnitude(Vector2 v, float m)
        {
            float mag = v.magnitude;
            if (mag <= m || mag == 0f) return v;
            return new Vector2(v.x / mag * m, v.y / mag * m);
        }
        public override string ToString() { return string.Format("({0:F2}, {1:F2})", x, y); }
    }

    public static class Mathf
    {
        public static float Exp(float f) { return (float)Math.Exp(f); }
        public static float Abs(float f) { return Math.Abs(f); }
        public static float Clamp01(float f) { return f < 0f ? 0f : (f > 1f ? 1f : f); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
        public static float MoveTowards(float a, float b, float d)
        {
            if (Math.Abs(b - a) <= d) return b;
            return a + Math.Sign(b - a) * d;
        }
    }
}
