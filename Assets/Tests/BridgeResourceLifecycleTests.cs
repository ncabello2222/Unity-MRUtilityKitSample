using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace NavigationSim.Tests
{
    /// <summary>
    /// Run in the Unity Test Runner's PlayMode tab. Reflection is intentional:
    /// NavigationSim's UI is in Assembly-CSharp, which an asmdef cannot reference.
    /// These tests exercise the real components, not copies of their algorithms.
    /// </summary>
    public class BridgeResourceLifecycleTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly List<Object> _owned = new List<Object>();

        private T Own<T>(T obj) where T : Object
        {
            _owned.Add(obj);
            return obj;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _owned.Count - 1; i >= 0; i--)
                if (_owned[i] != null) Object.DestroyImmediate(_owned[i]);
            _owned.Clear();
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName);
                if (type != null) return type;
            }
            throw new InvalidOperationException("Missing runtime type: " + fullName);
        }

        private static object Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Members).Invoke(target, args);

        private static object Get(object target, string property) =>
            target.GetType().GetProperty(property, Members).GetValue(target);

        private static object Field(object target, string field) =>
            target.GetType().GetField(field, Members).GetValue(target);

        [Test]
        public void ClosingPanelDisablesNestedCanvasAndButtonsButKeepsHierarchyWarm()
        {
            var root = Own(new GameObject("Panel test", typeof(RectTransform), typeof(Canvas)));
            var child = new GameObject("Nested", typeof(RectTransform), typeof(Canvas), typeof(BoxCollider));
            child.transform.SetParent(root.transform, false);
            var button = (Behaviour)child.AddComponent(FindType("NavigationSim.UnityLayer.UI.SimUiButton"));
            bool held = false;
            button.GetType().GetField("OnHoldChanged").SetValue(button, (Action<bool>)(value => held = value));

            var disabled = new GameObject("Initially disabled", typeof(BoxCollider));
            disabled.transform.SetParent(root.transform, false);
            disabled.GetComponent<BoxCollider>().enabled = false;
            var visibility = Activator.CreateInstance(FindType("NavigationSim.UnityLayer.UI.BridgePanelVisibility"), root);

            Call(button, "Press");
            Assert.IsTrue(held);
            Call(visibility, "SetVisible", false);
            Call(visibility, "SetVisible", false); // repeated Close must not overwrite saved states
            Assert.IsFalse(held, "A held NFU order must be released when hidden.");
            Assert.IsTrue(child.activeInHierarchy, "Do not rebuild the hierarchy on reopen.");
            Assert.IsFalse(root.GetComponent<Canvas>().enabled);
            Assert.IsFalse(child.GetComponent<Canvas>().enabled);
            Assert.IsFalse(child.GetComponent<BoxCollider>().enabled);
            Assert.IsFalse(button.enabled);
            Call(button, "Press");
            Assert.IsFalse(held, "A stale pointer must not press a hidden button.");

            Call(visibility, "SetVisible", true);
            Assert.IsTrue(root.GetComponent<Canvas>().enabled);
            Assert.IsTrue(child.GetComponent<Canvas>().enabled);
            Assert.IsTrue(child.GetComponent<BoxCollider>().enabled);
            Assert.IsTrue(button.enabled);
            Assert.IsFalse(disabled.GetComponent<BoxCollider>().enabled);

            // Closing from OnClick must not leave a late 'held=true' notification.
            button.GetType().GetField("OnClick").SetValue(button,
                (Action)(() => Call(visibility, "SetVisible", false)));
            Call(button, "Press");
            Assert.IsFalse(held);
        }

        [UnityTest]
        public IEnumerator OceanReusesStartupTexturesAndSafelyReinitializesPendingJobs()
        {
            var profile = Own(ScriptableObject.CreateInstance(FindType("Meta.Utilities.Environment.EnvironmentProfile")));
            var material = Resources.Load<Material>("NorthStarCalmOcean");
            Assert.IsNotNull(material);
            profile.GetType().GetField("<OceanMaterial>k__BackingField", Members).SetValue(profile, material);
            var root = Own(new GameObject("Ocean lifecycle test"));
            root.SetActive(false);
            var ocean = (Behaviour)root.AddComponent(FindType("Meta.Utilities.Environment.OceanSimulation"));
            ocean.GetType().GetField("m_resolution", Members).SetValue(ocean, 64);
            ocean.GetType().GetProperty("Profile").SetValue(ocean, profile);
            root.SetActive(true);

            var first = (Texture2D)Get(ocean, "DisplacementMap");
            Call(ocean, "UpdateSimulation", Vector3.forward * 8f);
            Assert.AreSame(first, Get(ocean, "DisplacementMap"), "First tick must not allocate a second ocean.");
            Call(ocean, "BeginContextRendering");

            Call(ocean, "UpdateSimulation", Vector3.forward * 8f);
            ocean.GetType().GetField("m_resolution", Members).SetValue(ocean, 128);
            Call(ocean, "UpdateSimulation", Vector3.forward * 8f);
            var resized = (Texture2D)Get(ocean, "DisplacementMap");
            Assert.AreEqual(128, resized.width);
            Call(ocean, "Initialize"); // public reinit with jobs still pending
            var reinitialized = (Texture2D)Get(ocean, "DisplacementMap");
            Assert.AreNotSame(resized, reinitialized);
            Call(ocean, "UpdateSimulation", Vector3.forward * 8f);
            Call(ocean, "Cleanup");
            Call(ocean, "Cleanup"); // must be safe before OnDisable repeats it
            Assert.IsNull(Get(ocean, "DisplacementMap"));
            Call(ocean, "BeginContextRendering"); // no upload into disposed textures
            ocean.enabled = false;
            Assert.IsNull(ocean.GetType().GetProperty("Instance").GetValue(null));
            ocean.enabled = true;
            Assert.IsNotNull(Get(ocean, "DisplacementMap"));
            Call(ocean, "UpdateSimulation", Vector3.forward * 8f);
            Call(ocean, "BeginContextRendering");
            yield return null;
            Assert.IsTrue(first == null);
            Assert.IsTrue(resized == null);
            Assert.IsTrue(reinitialized == null);
        }

        [UnityTest]
        public IEnumerator DestroyingRasterDisplaysReleasesTexturesAndDockedRoots()
        {
            foreach (string name in new[] { "BridgeChartDisplay", "BridgeRadarDisplay" })
            {
                var owner = Own(new GameObject(name + " test"));
                var display = (Behaviour)owner.AddComponent(FindType("NavigationSim.UnityLayer.UI." + name));
                display.enabled = false; // Build manually; do not start the live simulator.
                Call(display, "Build");
                var texture = (Texture2D)Field(display, "_tex");
                var canvas = Own((GameObject)Field(display, "_root"));
                canvas.transform.SetParent(null); // reproduce console docking ownership
                Assert.IsNotNull(texture);
                if (name == "BridgeRadarDisplay")
                    Assert.IsNotNull(canvas.transform.Find("PPI/Radials").GetComponent<Canvas>());
                Object.Destroy(display);
                yield return null;
                yield return null; // Destroy calls issued by OnDestroy finish end-of-frame.
                Assert.IsTrue(texture == null, name + " leaked its raster texture.");
                Assert.IsTrue(canvas == null, name + " left its docked canvas behind.");
            }
        }
    }
}
