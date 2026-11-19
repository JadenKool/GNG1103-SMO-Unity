// LanguageToggle.cs - put on a UI Toggle: on = French, off = English. Every LanguageToggle stays in sync.

using UnityEngine;
using UnityEngine.UI;

namespace Eosat
{
    [RequireComponent(typeof(Toggle))]
    public class LanguageToggle : MonoBehaviour
    {
        Toggle toggle;

        void Awake() => toggle = GetComponent<Toggle>();

        void OnEnable()
        {
            toggle.onValueChanged.AddListener(LocalizationManager.SetFrench);
            LocalizationManager.LanguageChanged += Sync;
            Sync();
        }

        void OnDisable()
        {
            toggle.onValueChanged.RemoveListener(LocalizationManager.SetFrench);
            LocalizationManager.LanguageChanged -= Sync;
        }

        void Sync() => toggle.SetIsOnWithoutNotify(LocalizationManager.IsFrench);
    }
}
