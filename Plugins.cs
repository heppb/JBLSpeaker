using BepInEx;
using System.IO;
using UnityEngine;

namespace JBLSpeaker
{
    [BepInPlugin("drb.jblspeaker", "JBLSpeaker", "1.1.0")]
    [BepInDependency(REPOLib.MyPluginInfo.PLUGIN_GUID, BepInDependency.DependencyFlags.HardDependency)]
    public class Plugins : BaseUnityPlugin
    {
        private void Awake()
        {
            Logger.LogInfo("JBLSpeaker Plugin Awake");
            string pluginFolderPath = Path.GetDirectoryName(Info.Location);
            string bundlePath = Path.Combine(pluginFolderPath, "jblspeaker.bundle");
            Logger.LogInfo("BundlePath = " + bundlePath.ToString());

            REPOLib.BundleLoader.LoadBundle(bundlePath, assetBundle =>
            {
                Logger.LogInfo("BundleLoader callback fired");

                if (assetBundle == null)
                {
                    Logger.LogError("AssetBundle is NULL");
                    return;
                }

                var prefab = assetBundle.LoadAsset<GameObject>("jblspeaker");

                if (prefab == null)
                {
                    Logger.LogError("JBLSpeaker prefab NOT found in bundle");
                    return;
                }

                Logger.LogInfo($"Prefab found: {prefab.name}");

                if (!prefab.TryGetComponent(out JBLSpeaker.Valuables.JBLSpeaker speaker))
                {
                    Logger.LogInfo("JBLSpeaker component NOT found on prefab — adding");

                    speaker = prefab.AddComponent<JBLSpeaker.Valuables.JBLSpeaker>();

                    speaker.tracks = new System.Collections.Generic.List<AudioSource>(
                        prefab.GetComponentsInChildren<AudioSource>(true)
                    );

                    speaker.particles = new System.Collections.Generic.List<ParticleSystem>(
                        prefab.GetComponentsInChildren<ParticleSystem>(true)
                    );
                }
                else
                {
                    Logger.LogInfo("JBLSpeaker component already exists on prefab");
                }
                Logger.LogInfo("Registering JBLSpeaker valuable");

                REPOLib.Modules.Valuables.RegisterValuable(prefab);
            });
        }
    }
}
