// LocalizationManager.cs - English / French UI text.
// English is the source language: every UI string is written in English, and Tr() swaps known English
// phrases for French (whole words only, longest phrase first). So rich-text markup and composed readouts
// ("ALT 500.9 km   SUN SUNLIT") translate without per-string keys.
//   Static labels:  add LocalizedText to the Text (the builder does this).
//   Code-set text:  text.text = LocalizationManager.Tr(english);
//   Switch:         LocalizationManager.SetLanguage("fr") / LanguageToggle on a UI Toggle.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Eosat
{
    public static class LocalizationManager
    {
        public const string English = "en", French = "fr";
        const string PrefKey = "eosat.language";

        static string language;
        public static string Language => language ??= Load();
        public static bool IsFrench => Language == French;
        /// <summary>Raised after the language changes; LocalizedText and code-driven readouts refresh on it.</summary>
        public static event Action LanguageChanged;

        // English phrase -> French. Matching is whole-word, so "ON" never hits "CONNECTION".
        static readonly Dictionary<string, string> Fr = new Dictionary<string, string>
        {
            // Screens / titles
            ["CONNECTION"] = "CONNEXION", ["CONNECTIONS"] = "CONNEXIONS", ["Connections"] = "Connexions",
            ["FLIGHT MAP"] = "CARTE DE VOL", ["ground track"] = "trace au sol",
            ["TELEMETRY DATA"] = "DONNÉES DE TÉLÉMÉTRIE", ["SATELLITE DATA"] = "DONNÉES SATELLITE",
            ["DATA TABLE"] = "TABLEAU DE DONNÉES", ["telemetry"] = "télémétrie", ["live values, 1 Hz"] = "valeurs en direct, 1 Hz",
            ["VR CONTROLS"] = "COMMANDES RV", ["MR CONTROLS"] = "COMMANDES RM",
            // Toggles / rows
            ["Data Overlay"] = "Superposition des données", ["Deploy Antenna"] = "Déployer l'antenne",
            ["Deploy Solar Panels"] = "Déployer le panneau solaire", ["Deploy Solar Panel"] = "Déployer le panneau solaire",
            ["Antenna"] = "Antenne", ["Solar Panels"] = "Panneau solaire", ["Solar Panel"] = "Panneau solaire",
            ["MR / VR View"] = "Vue RM / RV", ["FRANÇAIS"] = "FRANÇAIS", ["source, IP address"] = "source, adresse IP",
            ["ON"] = "ACTIVÉ", ["OFF"] = "DÉSACTIVÉ",
            ["DEPLOYED"] = "DÉPLOYÉ", ["DEPLOYING"] = "DÉPLOIEMENT", ["STOWED"] = "REPLIÉ", ["STOWING"] = "REPLIEMENT",
            ["MANUAL"] = "MANUEL", ["JAMMED"] = "BLOQUÉ",
            ["MR TABLETOP"] = "TABLE RM", ["VR ORBIT"] = "ORBITE RV", ["no passthrough (grey)"] = "pas de vue réelle (gris)",
            ["A / X recentre"] = "A / X recentrer", ["R stick orbit, L stick zoom"] = "stick D orbite, stick G zoom",
            // Connection
            ["Simulated (local)"] = "Simulé (local)", ["SET IP"] = "DÉFINIR IP", ["CLEAR"] = "EFFACER",
            ["CONNECTED"] = "CONNECTÉ", ["CONNECTING"] = "CONNEXION EN COURS", ["WAITING"] = "EN ATTENTE",
            ["Retrying every 2 s"] = "Nouvel essai toutes les 2 s", ["Built-in simulator / client replay"] = "Simulateur intégré / rejeu client",
            ["Socket open - waiting for first frame"] = "Socket ouvert - en attente de la première trame",
            ["good"] = "valides", ["rejected"] = "rejetées",
            ["Simulator IP address"] = "Adresse IP du simulateur", ["type, or use the keypad"] = "tapez, ou utilisez le pavé",
            ["TCP and WebSocket both use this address."] = "TCP et WebSocket utilisent tous deux cette adresse.",
            // Readouts
            ["Waiting for telemetry"] = "En attente de télémétrie", ["SUNLIT"] = "ENSOLEILLÉ", ["ECLIPSE"] = "ÉCLIPSE",
            ["NO CONTACT"] = "AUCUN CONTACT", ["POINT"] = "POINTAGE", ["SUN"] = "SOLEIL",
            ["ANTENNA"] = "ANTENNE", ["WINGS"] = "PANNEAU", ["PANEL"] = "PANNEAU", ["LINK"] = "LIAISON", ["since data"] = "depuis les données",
            // Data states / prediction
            ["PREDICTION MODE"] = "MODE PRÉDICTION", ["LIVE"] = "EN DIRECT", ["DELAYED"] = "RETARDÉ", ["GHOST"] = "FANTÔME", ["LOST"] = "PERDU",
            ["Telemetry lost"] = "Télémétrie perdue", ["s ago"] = "s",
            ["Showing <b>predicted</b> position from the last known orbit and spin"] = "Position <b>prédite</b> d'après la dernière orbite et rotation connues",
            ["Prediction limit reached - position <b>frozen</b> at the last estimate"] = "Limite de prédiction atteinte - position <b>figée</b> à la dernière estimation",
            ["Smooth 3 s re-join when data returns - reconnects automatically"] = "Retour en douceur (3 s) au retour des données - reconnexion automatique",
            // Loading
            ["BUFFERING TELEMETRY"] = "MISE EN MÉMOIRE DE LA TÉLÉMÉTRIE",
            ["Collecting 60 s of telemetry before the simulation starts"] = "Collecte de 60 s de télémétrie avant le début de la simulation",
            ["frames cached"] = "trames en mémoire", ["remaining"] = "restantes", ["No data yet"] = "Pas encore de données",
            // Data table keys
            ["Status"] = "État", ["Host"] = "Hôte", ["Packets"] = "Paquets", ["Rate"] = "Débit",
            ["Data state"] = "État des données", ["Last error"] = "Dernière erreur", ["Last reject"] = "Dernier rejet",
            ["Sim time"] = "Heure simulée", ["Position ECI"] = "Position ECI",
            ["Eclipse"] = "Éclipse", ["Pointing"] = "Pointage", ["Ground station"] = "Station sol",
            ["Antenna deploy"] = "Déploiement antenne", ["Buffer"] = "Mémoire tampon",
            ["none"] = "aucune", ["yes"] = "oui", ["no"] = "non",
            // Guides
            ["RIGHT STICK"] = "STICK DROIT", ["LEFT STICK"] = "STICK GAUCHE", ["TRIGGER"] = "GÂCHETTE", ["LEFT WRIST"] = "POIGNET GAUCHE",
            ["orbit the target - full 360°"] = "orbiter autour de la cible - 360°", ["up / down = zoom"] = "haut / bas = zoom",
            ["recentre on the target"] = "recentrer sur la cible", ["switch MR tabletop / VR orbit"] = "basculer table RM / orbite RV",
            ["iPad: telemetry, deploy, connections"] = "iPad : télémétrie, déploiement, connexions",
            ["grab the globe to spin it"] = "saisir le globe pour le faire tourner",
            ["point at a grab bar, then push / pull"] = "viser une barre de saisie, puis pousser / tirer",
            ["recentre the layout"] = "recentrer la disposition", ["GRIP"] = "POIGNÉE",
            ["Tilt the controller down to show this guide"] = "Inclinez la manette vers le bas pour afficher ce guide",
            ["CAMERA TARGET"] = "CIBLE CAMÉRA", ["FOLLOW EARTH"] = "SUIVRE LA TERRE", ["FOLLOW SATELLITE"] = "SUIVRE LE SATELLITE",
            ["Press to follow the satellite."] = "Appuyez pour suivre le satellite.",
            ["position only - it spins, you stay steady. Press for Earth."] = "position seulement - il tourne, vous restez stable. Appuyez pour la Terre.",
        };

        static Regex phrase;
        static Regex Phrase => phrase ??= new Regex(
            "(?<![A-Za-zÀ-ÿ])(?:" + string.Join("|", Fr.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)) + ")(?![A-Za-zÀ-ÿ])");

        /// <summary>English source text in the current language.</summary>
        public static string Tr(string english) =>
            !IsFrench || string.IsNullOrEmpty(english) ? english : Phrase.Replace(english, m => Fr[m.Value]);

        public static void SetLanguage(string lang)
        {
            lang = lang == French ? French : English;
            if (lang == Language) return;
            language = lang;
            PlayerPrefs.SetString(PrefKey, lang);
            LanguageChanged?.Invoke();
        }

        public static void SetFrench(bool french) => SetLanguage(french ? French : English);

        static string Load() => PlayerPrefs.GetString(PrefKey, English) == French ? French : English;
    }
}
