using System.IO;
using System.Runtime.InteropServices;
using Windows.AI.MachineLearning;

namespace WindowsDock;

internal static class LibraryImageClassifier
{
    internal static string[] Predict(byte[] pixels)
    {
        const int area = 224 * 224;
        if (pixels.Length != area * 4)
            return [];
        var folder = Path.Combine(AppContext.BaseDirectory, "ImageAnalysis");
        var path = Path.Combine(folder, "squeezenet.onnx");
        if (!File.Exists(path))
            return [];
        try
        {
            var labels = File.ReadAllLines(Path.Combine(folder, "labels.txt"));
            using var model = LearningModel.LoadFromFilePath(path);
            using var session = new LearningModelSession(model, new LearningModelDevice(LearningModelDeviceKind.Cpu));
            var bindings = new LearningModelBinding(session);
            var values = new float[3 * area];
            for (var pixel = 0; pixel < area; pixel++)
            {
                values[pixel] = (pixels[pixel * 4 + 2] / 255f - .485f) / .229f;
                values[area + pixel] = (pixels[pixel * 4 + 1] / 255f - .456f) / .224f;
                values[2 * area + pixel] = (pixels[pixel * 4] / 255f - .406f) / .225f;
            }
            using var tensor = TensorFloat.CreateFromArray([1, 3, 224, 224], values);
            Array.Clear(values);
            bindings.Bind(model.InputFeatures[0].Name, tensor);
            var result = session.Evaluate(bindings, "");
            if (result.Outputs[model.OutputFeatures[0].Name] is not TensorFloat output)
                return [];
            using (output)
            {
                var scores = output.GetAsVectorView();
                if (scores.Count != labels.Length)
                    return [];
                return Enumerable.Range(0, scores.Count).Where(index => scores[index] >= .18f)
                    .OrderByDescending(index => scores[index]).Take(3)
                    .Select(index => labels[index][(labels[index].IndexOf(',') + 1)..].Trim()).ToArray();
            }
        }
        catch (Exception error) when (error is COMException or IOException or InvalidOperationException or
            ArgumentException or UnauthorizedAccessException or NotSupportedException)
        { return []; }
    }
}
