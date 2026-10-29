namespace VarjoEye.Core.Services.Baballonia;

public sealed record BaballoniaOutputHead(double[] Weights, double[] Biases)
{
    public static BaballoniaOutputHead Identity { get; } = new(
        [1, 0, 0, 0, 0, 0,
         0, 1, 0, 0, 0, 0,
         0, 0, 1, 0, 0, 0,
         0, 0, 0, 1, 0, 0,
         0, 0, 0, 0, 1, 0,
         0, 0, 0, 0, 0, 1],
        [0, 0, 0, 0, 0, 0]);

    public bool IsPlausible
    {
        get
        {
            if (Weights is not { Length: 36 } || Biases is not { Length: 6 }) return false;
            for (var output = 0; output < 6; output++)
            {
                if (!double.IsFinite(Biases[output]) || Math.Abs(Biases[output]) > 0.7)
                    return false;
                var offDiagonal = 0d;
                for (var input = 0; input < 6; input++)
                {
                    var weight = Weights[output * 6 + input];
                    if (!double.IsFinite(weight) || Math.Abs(weight) > 2) return false;
                    if (input == output)
                    {
                        if (Math.Abs(weight) < 0.05) return false;
                    }
                    else offDiagonal += Math.Abs(weight);
                }
                if (offDiagonal > 2) return false;
            }
            return true;
        }
    }

    public void Apply(Span<float> values)
    {
        Span<float> input = stackalloc float[6];
        values.CopyTo(input);
        for (var output = 0; output < 6; output++)
        {
            var sum = Biases[output];
            for (var i = 0; i < 6; i++)
                sum += Weights[output * 6 + i] * input[i];
            values[output] = (float)Math.Clamp(sum, 0, 1);
        }
    }
}
