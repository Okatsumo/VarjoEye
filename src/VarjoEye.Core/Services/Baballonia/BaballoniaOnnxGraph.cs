using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace VarjoEye.Core.Services.Baballonia;

/// <summary>
/// Minimal ONNX editor for the pinned Baballonia eye model: expose Flatten
/// features and replace the 3×212 last-layer Gemm tensors.
/// </summary>
public static class BaballoniaOnnxGraph
{
    public const int FeatureSize = 212;
    public const int HeadOutputs = 3;
    public const string LeftFlatten = "/left/Flatten_output_0";
    public const string RightFlatten = "/right/Flatten_output_0";
    public const string GazeOutput = "output";
    private const string LeftWeight = "left.fc.weight";
    private const string LeftBias = "left.fc.bias";
    private const string RightWeight = "right.fc.weight";
    private const string RightBias = "right.fc.bias";

    public readonly record struct HeadTensors(
        float[] LeftWeight, float[] LeftBias, float[] RightWeight, float[] RightBias);

    public static byte[] WithFlattenOutputs(byte[] model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var graph = GetGraph(model, out var graphStart, out var graphLength);
        if (CountGraphOutputs(graph) >= 3) return model;

        var extra = Concat(EncodeGraphOutput(LeftFlatten, FeatureSize),
            EncodeGraphOutput(RightFlatten, FeatureSize));
        var newGraph = new byte[graphLength + extra.Length];
        Buffer.BlockCopy(model, graphStart, newGraph, 0, graphLength);
        Buffer.BlockCopy(extra, 0, newGraph, graphLength, extra.Length);
        return RewriteGraph(model, graphStart, graphLength, newGraph);
    }

    public static HeadTensors ReadHead(byte[] model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return new HeadTensors(
            ReadTensor(model, LeftWeight, HeadOutputs * FeatureSize),
            ReadTensor(model, LeftBias, HeadOutputs),
            ReadTensor(model, RightWeight, HeadOutputs * FeatureSize),
            ReadTensor(model, RightBias, HeadOutputs));
    }

    public static byte[] WriteHead(byte[] model, HeadTensors head)
    {
        ArgumentNullException.ThrowIfNull(model);
        var copy = (byte[])model.Clone();
        WriteTensor(copy, LeftWeight, head.LeftWeight);
        WriteTensor(copy, LeftBias, head.LeftBias);
        WriteTensor(copy, RightWeight, head.RightWeight);
        WriteTensor(copy, RightBias, head.RightBias);
        return copy;
    }

    private static float[] ReadTensor(byte[] model, string name, int floats)
    {
        var raw = FindTensorRaw(model, name, floats * 4);
        var values = new float[floats];
        for (var i = 0; i < floats; i++)
            values[i] = BitConverter.ToSingle(raw.Slice(i * 4, 4));
        return values;
    }

    private static void WriteTensor(byte[] model, string name, float[] values)
    {
        var raw = FindTensorRaw(model, name, values.Length * 4);
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(raw.Slice(i * 4, 4), values[i]);
    }

    private static Span<byte> FindTensorRaw(byte[] model, string name, int rawBytes)
    {
        var needle = Encoding.ASCII.GetBytes(name);
        var start = 0;
        while (true)
        {
            var index = IndexOf(model, needle, start);
            if (index < 0)
                throw new InvalidOperationException($"ONNX tensor '{name}' was not found.");
            // TensorProto name is field 8 (tag 0x42) immediately before the bytes.
            if (index >= 2 && model[index - 1] == needle.Length && model[index - 2] == 0x42)
            {
                var cursor = index + needle.Length;
                if (cursor < model.Length && model[cursor] == 0x4A)
                {
                    cursor++;
                    var length = ReadVarint(model, ref cursor);
                    if (length == rawBytes && cursor + length <= model.Length)
                        return model.AsSpan(cursor, rawBytes);
                }
            }
            start = index + 1;
        }
    }

    private static byte[] EncodeGraphOutput(string name, int lastDim)
    {
        var valueInfo = EncodeValueInfo(name, lastDim);
        return Concat(new byte[] { 0x62 }, EncodeVarint(valueInfo.Length), valueInfo);
    }

    private static byte[] EncodeValueInfo(string name, int lastDim)
    {
        var nameBytes = Encoding.ASCII.GetBytes(name);
        var batchDim = Concat(new byte[] { 0x12, 0x0A }, Encoding.ASCII.GetBytes("batch_size"));
        var batch = Concat(new byte[] { 0x0A }, EncodeVarint(batchDim.Length), batchDim);
        var last = Concat(new byte[] { 0x08 }, EncodeVarint(lastDim));
        var lastWrapped = Concat(new byte[] { 0x0A }, EncodeVarint(last.Length), last);
        var shape = Concat(batch, lastWrapped);
        var tensor = Concat(new byte[] { 0x08, 0x01, 0x12 }, EncodeVarint(shape.Length), shape);
        var type = Concat(new byte[] { 0x0A }, EncodeVarint(tensor.Length), tensor);
        var nameField = Concat(new byte[] { 0x0A }, EncodeVarint(nameBytes.Length), nameBytes);
        var typeField = Concat(new byte[] { 0x12 }, EncodeVarint(type.Length), type);
        return Concat(nameField, typeField);
    }

    private static byte[] RewriteGraph(byte[] model, int graphStart, int graphLength, byte[] newGraph)
    {
        var lengthStart = graphStart - 1;
        while (lengthStart > 0 && (model[lengthStart - 1] & 0x80) != 0)
            lengthStart--;
        if (lengthStart == 0 || model[lengthStart - 1] != 0x3A)
            throw new InvalidOperationException("ONNX graph field header is invalid.");
        var prefix = model.AsSpan(0, lengthStart);
        var suffix = model.AsSpan(graphStart + graphLength);
        var lengthBytes = EncodeVarint(newGraph.Length);
        var result = new byte[prefix.Length + lengthBytes.Length + newGraph.Length + suffix.Length];
        prefix.CopyTo(result);
        lengthBytes.CopyTo(result.AsSpan(prefix.Length));
        newGraph.CopyTo(result.AsSpan(prefix.Length + lengthBytes.Length));
        suffix.CopyTo(result.AsSpan(prefix.Length + lengthBytes.Length + newGraph.Length));
        return result;
    }

    private static byte[] GetGraph(byte[] model, out int start, out int length)
    {
        var cursor = 0;
        while (cursor < model.Length)
        {
            var key = ReadVarint(model, ref cursor);
            var field = key >> 3;
            var wire = key & 7;
            if (wire == 2)
            {
                var size = ReadVarint(model, ref cursor);
                if (field == 7)
                {
                    start = cursor;
                    length = size;
                    return model.AsSpan(cursor, size).ToArray();
                }
                cursor += size;
            }
            else if (wire == 0) ReadVarint(model, ref cursor);
            else if (wire == 1) cursor += 8;
            else if (wire == 5) cursor += 4;
            else throw new InvalidOperationException("Unsupported ONNX protobuf wire type.");
        }
        throw new InvalidOperationException("ONNX graph is missing.");
    }

    private static int CountGraphOutputs(byte[] graph)
    {
        var cursor = 0;
        var count = 0;
        while (cursor < graph.Length)
        {
            var key = ReadVarint(graph, ref cursor);
            var field = key >> 3;
            var wire = key & 7;
            if (wire == 2)
            {
                var size = ReadVarint(graph, ref cursor);
                if (field == 12) count++;
                cursor += size;
            }
            else if (wire == 0) ReadVarint(graph, ref cursor);
            else if (wire == 1) cursor += 8;
            else if (wire == 5) cursor += 4;
            else throw new InvalidOperationException("Unsupported ONNX graph wire type.");
        }
        return count;
    }

    private static int IndexOf(byte[] data, byte[] needle, int start)
    {
        var last = data.Length - needle.Length;
        for (var i = start; i <= last; i++)
        {
            var found = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (data[i + j] == needle[j]) continue;
                found = false;
                break;
            }
            if (found) return i;
        }
        return -1;
    }

    private static int ReadVarint(byte[] data, ref int cursor)
    {
        var value = 0;
        var shift = 0;
        while (cursor < data.Length)
        {
            var b = data[cursor++];
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
            shift += 7;
            if (shift > 28) throw new InvalidOperationException("Varint is too large.");
        }
        throw new InvalidOperationException("Truncated varint.");
    }

    private static byte[] EncodeVarint(int value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        var bytes = new List<byte>(5);
        var v = value;
        while (v >= 0x80)
        {
            bytes.Add((byte)(v | 0x80));
            v >>= 7;
        }
        bytes.Add((byte)v);
        return bytes.ToArray();
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var length = 0;
        foreach (var part in parts) length += part.Length;
        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            Buffer.BlockCopy(part, 0, result, offset, part.Length);
            offset += part.Length;
        }
        return result;
    }
}
