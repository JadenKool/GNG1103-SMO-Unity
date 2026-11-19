// LocalizedText.cs - put on a static UI Text. Remembers its English text and re-applies it in the
// current language whenever LocalizationManager.LanguageChanged fires (and on enable).

using UnityEngine;
using UnityEngine.UI;

namespace Eosat
{
    [RequireComponent(typeof(Text))]
    public class LocalizedText : MonoBehaviour
    {
        [Tooltip("English source text. Empty = take the Text's own text at start.")]
        [TextArea] public string english;
        Text text;

        void Awake()
        {
            text = GetComponent<Text>();
            if (string.IsNullOrEmpty(english)) english = text.text;
        }

        void OnEnable() { LocalizationManager.LanguageChanged += Apply; Apply(); }
        void OnDisable() => LocalizationManager.LanguageChanged -= Apply;

        void Apply() { if (text) text.text = LocalizationManager.Tr(english); }
    }
}
