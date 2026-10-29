namespace VarjoEye.Core.Services.Baballonia;

internal static class LinearSystemSolver
{
    public static double[]? Solve(double[,] matrix, double[] values)
    {
        var n = values.Length;
        var a = new double[n, n + 1];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++) a[i, j] = matrix[i, j];
            a[i, n] = values[i];
        }
        for (var k = 0; k < n; k++)
        {
            var pivot = k;
            for (var i = k + 1; i < n; i++)
                if (Math.Abs(a[i, k]) > Math.Abs(a[pivot, k])) pivot = i;
            if (Math.Abs(a[pivot, k]) < 1e-9) return null;
            if (pivot != k)
            {
                for (var j = k; j <= n; j++)
                    (a[k, j], a[pivot, j]) = (a[pivot, j], a[k, j]);
            }
            var diagonal = a[k, k];
            for (var j = k; j <= n; j++) a[k, j] /= diagonal;
            for (var i = 0; i < n; i++)
            {
                if (i == k) continue;
                var factor = a[i, k];
                for (var j = k; j <= n; j++) a[i, j] -= factor * a[k, j];
            }
        }
        var solution = new double[n];
        for (var i = 0; i < n; i++)
        {
            if (!double.IsFinite(a[i, n])) return null;
            solution[i] = a[i, n];
        }
        return solution;
    }
}
