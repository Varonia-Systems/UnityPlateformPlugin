// Provider tuto du package principal "VBO Classique" (Varonia Back Office).
//
// Pour l'instant : une fiche d'aperçu qui pointe vers les outils existants.
// À enrichir composant par composant — il suffit d'ajouter des AddCard(...).

using VaroniaBackOffice.Tutorials;

namespace VaroniaBackOffice.EditorTools.Tutorials
{
    public class VboClassiqueTutorialProvider : IVaroniaTutorialProvider
    {
        public TutorialPackage GetPackage()
        {
            var pkg = new TutorialPackage(
                id: "com.varonia.backofficeultimate",
                displayName: "VBO Classique",
                description: "Le Back Office Varonia : build, config, périphériques PICO, tracking…",
                order: 10);

            pkg.AddCard(new TutorialCard("Aperçu du Back Office", "Vue d'ensemble",
                    "Le hub des outils Varonia, accessibles depuis le menu Varonia.")
                .Desc(
                    "Le package <b>Varonia Back Office</b> regroupe les outils d'intégration à la plateforme Varonia :\n" +
                    "build du jeu, configuration (Game / Global / Spatial), monitoring des casques PICO, " +
                    "tracking d'armes et bridge OpenVR.\n\n" +
                    "Les tutoriels détaillés de ce package arriveront ici, fiche par fiche.")
                .Use(
                    "Builder et packager le jeu pour la plateforme",
                    "Configurer le GameID et les chemins du projet",
                    "Brancher / monitorer un casque PICO en ADB")
                .Act(TutorialActions.Custom("⚙ Ouvrir le menu Varonia",
                    () => UnityEditor.EditorUtility.DisplayDialog(
                        "Menu Varonia",
                        "Ouvre la barre de menus « Varonia » en haut de l'éditeur pour accéder aux outils " +
                        "(Build Menu, Project Settings, SpatialConfig, PICO Device…).",
                        "OK"),
                    "Rappel : les outils sont dans la barre de menus Varonia.")));

            return pkg;
        }
    }
}
