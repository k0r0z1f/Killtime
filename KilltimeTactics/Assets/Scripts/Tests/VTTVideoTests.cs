#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;
using Killtime.Multi;

namespace Killtime.Multi.Tests
{
    [TestFixture]
    public class VTTVideoTests
    {
        [Test]
        public void VTTProtocol_VideoOpSerialization()
        {
            string base64Sample = "/9j/4AAQSkZJRgABAQEASABIAAD";
            string json = VTTProtocol.BuildVideoOp(240, 180, 12, base64Sample);

            StringAssert.Contains("\"type\":\"op\"", json);
            StringAssert.Contains("\"op\":\"video\"", json);
            StringAssert.Contains("\"width\":240", json);
            StringAssert.Contains("\"height\":180", json);
            StringAssert.Contains("\"seq\":12", json);
            StringAssert.Contains($"\"data\":\"{base64Sample}\"", json);

            string payloadJson = VTTProtocol.BuildVideoPayloadJson(160, 120, 5, base64Sample);
            var payload = JsonUtility.FromJson<VTTVideoPayload>(payloadJson);
            Assert.IsNotNull(payload);
            Assert.AreEqual(160, payload.width);
            Assert.AreEqual(120, payload.height);
            Assert.AreEqual(5, payload.seq);
            Assert.AreEqual(base64Sample, payload.data);
        }

        [Test]
        public void VTTVideo_SentisModel_CanLoadAndInspect()
        {
            var modelAsset = Resources.Load<Unity.InferenceEngine.ModelAsset>("selfie_segmentation_landscape")
                          ?? Resources.Load<Unity.InferenceEngine.ModelAsset>("selfie_segmentation")
                          ?? Resources.Load<Unity.InferenceEngine.ModelAsset>("model");

            Assert.IsNotNull(modelAsset, "Le modèle ONNX de segmentation selfie doit être présent dans Assets/Resources/.");

            var model = Unity.InferenceEngine.ModelLoader.Load(modelAsset);
            Assert.IsNotNull(model);
            Assert.Greater(model.inputs.Count, 0, "Le modèle doit avoir au moins une entrée.");
            Assert.Greater(model.outputs.Count, 0, "Le modèle doit avoir au moins une sortie.");

            var inShape = model.inputs[0].shape.ToTensorShape();
            Assert.AreEqual(4, inShape.rank, "Le tenseur d'entrée doit être de rang 4.");

            int inW = inShape[3] == 3 ? inShape[2] : inShape[3];
            int inH = inShape[3] == 3 ? inShape[1] : inShape[2];
            Assert.AreEqual(256, inW, "La largeur d'entrée du modèle doit être 256.");
            Assert.AreEqual(144, inH, "La hauteur d'entrée du modèle landscape doit être 144.");
        }

        [Test]
        public void VTTVideo_SentisWorker_RunsInferenceOnCPU()
        {
            var modelAsset = Resources.Load<Unity.InferenceEngine.ModelAsset>("selfie_segmentation_landscape")
                          ?? Resources.Load<Unity.InferenceEngine.ModelAsset>("selfie_segmentation")
                          ?? Resources.Load<Unity.InferenceEngine.ModelAsset>("model");

            if (modelAsset == null)
            {
                Assert.Ignore("Modèle ONNX non trouvé sous Resources.");
                return;
            }

            var model = Unity.InferenceEngine.ModelLoader.Load(modelAsset);
            var shape = model.inputs[0].shape.ToTensorShape();

            using var worker = new Unity.InferenceEngine.Worker(model, Unity.InferenceEngine.BackendType.CPU);
            float[] sampleInput = new float[shape.length];
            for (int i = 0; i < sampleInput.Length; i++)
            {
                sampleInput[i] = 0.5f;
            }

            using var tensor = new Unity.InferenceEngine.Tensor<float>(shape, sampleInput);
            worker.Schedule(tensor);

            var outputTensor = worker.PeekOutput() as Unity.InferenceEngine.Tensor<float>;
            Assert.IsNotNull(outputTensor, "La sortie Sentis ne doit pas être nulle.");

            float[] rawMask = outputTensor.DownloadToArray();
            Assert.IsNotNull(rawMask);
            Assert.AreEqual(144 * 256, rawMask.Length, "La sortie du masque doit faire 144x256 (36864 éléments).");

            for (int i = 0; i < Mathf.Min(100, rawMask.Length); i++)
            {
                Assert.IsTrue(rawMask[i] >= -0.01f && rawMask[i] <= 1.01f, $"La probabilité du masque ({rawMask[i]}) doit être entre 0 et 1.");
            }
        }
    }
}
#endif