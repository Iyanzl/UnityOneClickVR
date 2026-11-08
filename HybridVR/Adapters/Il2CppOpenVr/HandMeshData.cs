using System;
using System.IO;

namespace HybridVR.Adapters.Il2CppOpenVr;

internal sealed class HandMeshData
{
    internal readonly float[][] Poses;
    internal readonly int[] Triangles;
    internal int VertexCount => Poses[0].Length / 3;
    private HandMeshData(float[][] poses, int[] triangles) { Poses = poses; Triangles = triangles; }
    internal static HandMeshData[] Read(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        if (reader.ReadUInt32() != 0x31485648 || reader.ReadInt32() != 2) throw new InvalidDataException("Invalid hand mesh header");
        var hands = new HandMeshData[2];
        for (int hand = 0; hand < 2; hand++)
        {
            int vertices = reader.ReadInt32(), count = reader.ReadInt32();
            if (vertices < 3 || vertices > 65535 || count < 3 || count > 300000 || count % 3 != 0) throw new InvalidDataException("Invalid hand mesh size");
            var poses = new float[4][];
            for (int pose = 0; pose < poses.Length; pose++)
            {
                poses[pose] = new float[vertices * 3];
                for (int i = 0; i < poses[pose].Length; i++)
                {
                    float value = reader.ReadSingle();
#if HYBRID_MONO
                    if (!HybridVR.Adapters.Mono.FrameworkCompatibility.IsFinite(value) || Math.Abs(value) > 1) throw new InvalidDataException("Invalid hand vertex");
#else
                    if (!float.IsFinite(value) || Math.Abs(value) > 1) throw new InvalidDataException("Invalid hand vertex");
#endif
                    poses[pose][i] = value;
                }
            }
            var triangles = new int[count];
            for (int i = 0; i < count; i++)
            { triangles[i] = reader.ReadInt32(); if (triangles[i] < 0 || triangles[i] >= vertices) throw new InvalidDataException("Invalid hand index"); }
            hands[hand] = new HandMeshData(poses, triangles);
        }
        if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Unexpected hand mesh data");
        return hands;
    }
    internal float Coordinate(int index, float grip, float trigger)
    {
#if HYBRID_MONO
        grip = HybridVR.Adapters.Mono.FrameworkCompatibility.Clamp(grip, 0, 1); trigger = HybridVR.Adapters.Mono.FrameworkCompatibility.Clamp(trigger, 0, 1);
#else
        grip = Math.Clamp(grip, 0, 1); trigger = Math.Clamp(trigger, 0, 1);
#endif
        float a = Poses[0][index] + (Poses[1][index] - Poses[0][index]) * grip;
        float b = Poses[2][index] + (Poses[3][index] - Poses[2][index]) * grip;
        return a + (b - a) * trigger;
    }
}
