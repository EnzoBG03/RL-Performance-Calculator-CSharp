using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;

namespace RL_Performance_Calculator;

public partial class MainWindow : Window
{
    // Constantes du programme original
    private const int CADRE = 10;
    private const int TOUCHE = 2;
    private const int ARRET = 50;
    private const int PASSE_DECISIVE = 50;
    private const int BONUS_SAUVE_MIRACLE = 25;
    private const int BUT = 100;

    // Classe StatistiquesJoueur du programme original
    public class StatistiquesJoueur
    {
        public string Nom { get; set; } = "";
        public int NbTouche { get; set; }
        public int NbCadre { get; set; }
        public int NbArret { get; set; }
        public int NbMiracle { get; set; }
        public int NbDecisive { get; set; }
        public int NbBut { get; set; }
        public int ScoreArrets { get; set; }
        public int ScoreTotal { get; set; }

        // Renseignés au moment d'afficher le classement (#1, #2... et médaille d'or du premier)
        public int Rang { get; set; }
        public bool EstPremier { get; set; }
    }

    // Dictionnaire conservé du programme original
    private readonly Dictionary<string, StatistiquesJoueur> statistiquesJoueurs = new();

    public MainWindow()
    {
        InitializeComponent();

        // Fondu d'entrée : la transition d'Opacity est déclarée dans le XAML
        Opened += (_, _) => RootGrid.Opacity = 1;
    }

    // ------------------------------------------------------------------
    // Thème clair / sombre (bouton lune / soleil de l'en-tête, comme sur le site)
    // ------------------------------------------------------------------

    private bool _changementThemeEnCours;

    private async void ToggleTheme_Click(object? sender, RoutedEventArgs e)
    {
        if (_changementThemeEnCours) return;
        _changementThemeEnCours = true;

        // 1. Photo de l'écran actuel (null si la capture échoue : le thème change alors sans fondu)
        RenderTargetBitmap? capture = CapturerEcran();

        if (capture != null)
        {
            // Affiche la photo par-dessus l'interface, opaque, sans transition
            var transitions = ThemeSnapshot.Transitions;
            ThemeSnapshot.Transitions = null;
            ThemeSnapshot.Opacity = 1;
            ThemeSnapshot.Transitions = transitions;
            ThemeSnapshot.Source = capture;
            ThemeSnapshot.IsVisible = true;
        }

        // 2. Changement de thème (invisible : la photo de l'ancien thème le recouvre)
        bool versSombre = ActualThemeVariant != ThemeVariant.Dark;

        RequestedThemeVariant = versSombre ? ThemeVariant.Dark : ThemeVariant.Light;

        // Comme sur le site : la lune propose le mode sombre, le soleil propose le mode clair
        MoonIcon.IsVisible = !versSombre;
        SunIcon.IsVisible = versSombre;

        if (capture != null)
        {
            // 3. Fondu enchaîné : l'ancienne image s'efface et révèle le nouveau thème
            await Task.Delay(60); // laisse le nouveau thème se dessiner dessous
            ThemeSnapshot.Opacity = 0;
            await Task.Delay(500); // durée de la transition (0,45 s) + marge

            ThemeSnapshot.IsVisible = false;
            ThemeSnapshot.Source = null;
            capture.Dispose();
        }

        _changementThemeEnCours = false;
    }

    private RenderTargetBitmap? CapturerEcran()
    {
        try
        {
            double echelle = RenderScaling;
            var taille = new PixelSize(
                Math.Max(1, (int)Math.Ceiling(RootGrid.Bounds.Width * echelle)),
                Math.Max(1, (int)Math.Ceiling(RootGrid.Bounds.Height * echelle)));

            var bitmap = new RenderTargetBitmap(taille, new Vector(96 * echelle, 96 * echelle));
            bitmap.Render(RootGrid);
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Animations (remplacent les Storyboard WPF)
    // ------------------------------------------------------------------

    /// <summary>
    /// Fondu + glissement horizontal optionnel. La cible doit avoir un TranslateTransform
    /// comme RenderTransform si <paramref name="decalageX"/> est renseigné.
    /// </summary>
    private static void Animer(Control cible, double? decalageX, int dureeMs, int delaiMs = 0, bool rebond = false)
    {
        Easing easing = rebond ? new BackEaseOut() : new QuadraticEaseOut();

        var debut = new KeyFrame { Cue = new Cue(0d) };
        var fin = new KeyFrame { Cue = new Cue(1d) };

        debut.Setters.Add(new Setter(Visual.OpacityProperty, 0d));
        fin.Setters.Add(new Setter(Visual.OpacityProperty, 1d));

        if (decalageX is double x)
        {
            debut.Setters.Add(new Setter(TranslateTransform.XProperty, x));
            fin.Setters.Add(new Setter(TranslateTransform.XProperty, 0d));
        }

        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(dureeMs),
            Delay = TimeSpan.FromMilliseconds(delaiMs),
            Easing = easing,
            FillMode = FillMode.Backward,
            Children = { debut, fin }
        };

        _ = animation.RunAsync(cible);
    }

    /// <summary>
    /// Exécute l'action une fois le nouvel onglet affiché : en Avalonia, le contenu d'un onglet
    /// n'est attaché à l'arbre visuel qu'au moment où il est sélectionné.
    /// </summary>
    private static void ApresAffichage(Action action) =>
        Dispatcher.UIThread.Post(action, DispatcherPriority.Loaded);

    // ------------------------------------------------------------------
    // Gestionnaires d'événements
    // ------------------------------------------------------------------

    private void CommencerSaisie_Click(object? sender, RoutedEventArgs e)
    {
        InputTab.IsEnabled = true;
        MainTabControl.SelectedItem = InputTab;

        ApresAffichage(() =>
        {
            // Formulaire depuis la gauche, carte de droite avec un léger délai
            Animer(FormulaireGroup, -50, 400, rebond: true);
            Animer(ListeGroup, 50, 400, delaiMs: 200, rebond: true);

            NomJoueurTextBox.Focus();
        });
    }

    /// <summary>
    /// Comme sur le site, un champ vide vaut 0. Retourne false si le texte n'est pas un entier positif.
    /// </summary>
    private static bool TryLireEntier(string? texte, out int valeur)
    {
        if (string.IsNullOrWhiteSpace(texte))
        {
            valeur = 0;
            return true;
        }

        return int.TryParse(texte.Trim(), out valeur) && valeur >= 0;
    }

    private void AjouterJoueur_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            MasquerMessage();

            // Validation du nom
            string nomJoueur = (NomJoueurTextBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(nomJoueur))
            {
                AfficherErreur("Veuillez entrer un nom de joueur.");
                return;
            }

            // Validation et conversion des statistiques
            if (!TryLireEntier(TouchesTextBox.Text, out int nbTouche))
            {
                AfficherErreur("Le nombre de touches doit être un nombre entier positif.");
                return;
            }

            if (!TryLireEntier(CadresTextBox.Text, out int nbCadre))
            {
                AfficherErreur("Le nombre de tirs cadrés doit être un nombre entier positif.");
                return;
            }

            if (!TryLireEntier(ArretsTextBox.Text, out int nbArret))
            {
                AfficherErreur("Le nombre d'arrêts doit être un nombre entier positif.");
                return;
            }

            if (!TryLireEntier(MiraclesTextBox.Text, out int nbMiracle))
            {
                AfficherErreur("Le nombre de sauvetages miraculeux doit être un nombre entier positif.");
                return;
            }

            if (!TryLireEntier(PassesTextBox.Text, out int nbDecisive))
            {
                AfficherErreur("Le nombre de passes décisives doit être un nombre entier positif.");
                return;
            }

            if (!TryLireEntier(ButsTextBox.Text, out int nbBut))
            {
                AfficherErreur("Le nombre de buts doit être un nombre entier positif.");
                return;
            }

            // Validation logique : les miracles ne peuvent pas dépasser les arrêts
            if (nbMiracle > nbArret)
            {
                AfficherErreur("Le nombre de sauvetages miraculeux ne peut pas être supérieur au nombre d'arrêts.");
                return;
            }

            var stats = new StatistiquesJoueur
            {
                Nom = nomJoueur,
                NbTouche = nbTouche,
                NbCadre = nbCadre,
                NbArret = nbArret,
                NbMiracle = nbMiracle,
                NbDecisive = nbDecisive,
                NbBut = nbBut
            };

            // Calculs conservés du programme original
            stats.ScoreArrets = (stats.NbArret * ARRET) + (stats.NbMiracle * BONUS_SAUVE_MIRACLE);
            stats.ScoreTotal = CalculerScore(stats);

            // Gestion des joueurs existants (logique du programme original)
            if (!statistiquesJoueurs.TryGetValue(nomJoueur, out var existingStats))
            {
                statistiquesJoueurs[nomJoueur] = stats;
            }
            else
            {
                existingStats.NbTouche += stats.NbTouche;
                existingStats.NbCadre += stats.NbCadre;
                existingStats.NbArret += stats.NbArret;
                existingStats.NbMiracle += stats.NbMiracle;
                existingStats.NbDecisive += stats.NbDecisive;
                existingStats.NbBut += stats.NbBut;
                existingStats.ScoreArrets += stats.ScoreArrets;
                existingStats.ScoreTotal += stats.ScoreTotal;
            }

            MettreAJourListeJoueurs();
            ViderFormulaireJoueur();

            // Activer le bouton des résultats s'il y a au moins un joueur
            VoirResultatsButton.IsEnabled = statistiquesJoueurs.Count > 0;

            AfficherSucces($"{nomJoueur} a été ajouté avec succès !");
        }
        catch (Exception ex)
        {
            AfficherErreur($"Erreur lors de l'ajout du joueur : {ex.Message}");
        }
    }

    // ------------------------------------------------------------------
    // Message sous le formulaire (encart rouge d'erreur ou bleu de succès)
    // ------------------------------------------------------------------

    private void MasquerMessage()
    {
        MessageTextBlock.Text = "";
        MessageBorder.IsVisible = false;
    }

    private void AfficherMessage(string message, bool succes)
    {
        string aRetirer = succes ? "error" : "success";
        string aAjouter = succes ? "success" : "error";

        MessageBorder.Classes.Remove(aRetirer);
        if (!MessageBorder.Classes.Contains(aAjouter))
        {
            MessageBorder.Classes.Add(aAjouter);
        }

        MessageTextBlock.Text = message;
        MessageBorder.IsVisible = true;

        if (succes)
        {
            Animer(MessageBorder, null, 300);
        }
    }

    private void AfficherErreur(string message) => AfficherMessage(message, succes: false);

    private void AfficherSucces(string message) => AfficherMessage(message, succes: true);

    // Méthode CalculerScore conservée du programme original
    private static int CalculerScore(StatistiquesJoueur stats)
    {
        return (TOUCHE * stats.NbTouche) + (CADRE * stats.NbCadre) + stats.ScoreArrets +
               (PASSE_DECISIVE * stats.NbDecisive) + (BUT * stats.NbBut);
    }

    private void ViderFormulaireJoueur()
    {
        // Champs vides : les placeholders du site (« Ballons touchés », etc.) réapparaissent
        NomJoueurTextBox.Text = "";
        TouchesTextBox.Text = "";
        CadresTextBox.Text = "";
        ArretsTextBox.Text = "";
        MiraclesTextBox.Text = "";
        PassesTextBox.Text = "";
        ButsTextBox.Text = "";
        NomJoueurTextBox.Focus();
    }

    private void MettreAJourListeJoueurs()
    {
        // Nouvelle liste à chaque fois pour rafraîchir l'affichage
        JoueursItems.ItemsSource = statistiquesJoueurs.Values.ToList();

        // Compteur « Joueurs inscrits » ; la « Feuille de match » n'apparaît qu'avec au moins un joueur
        CompteurJoueursText.Text = statistiquesJoueurs.Count.ToString();
        RosterBox.IsVisible = statistiquesJoueurs.Count > 0;
    }

    private void VoirResultats_Click(object? sender, RoutedEventArgs e)
    {
        if (statistiquesJoueurs.Count == 0)
        {
            // Le bouton est désactivé tant qu'il n'y a aucun joueur
            AfficherErreur("Aucun joueur n'a été ajouté.");
            return;
        }

        AfficherResultats();

        ResultsTab.IsEnabled = true;
        MainTabControl.SelectedItem = ResultsTab;

        ApresAffichage(() => Animer(ResultatsPanel, -50, 400, rebond: true));
    }

    private void AfficherResultats()
    {
        if (statistiquesJoueurs.Count == 0) return;

        var meilleurJoueur = statistiquesJoueurs.OrderByDescending(kvp => kvp.Value.ScoreTotal).First();
        var meilleurButeur = statistiquesJoueurs.OrderByDescending(kvp => kvp.Value.NbBut).First();
        var meilleurPasseur = statistiquesJoueurs.OrderByDescending(kvp => kvp.Value.NbDecisive).First();
        var meilleurGardien = statistiquesJoueurs.OrderByDescending(kvp => kvp.Value.NbArret).First();

        // Mêmes libellés que le site : « Nom (N pts) »
        MeilleurJoueurText.Text = $"{meilleurJoueur.Key} ({meilleurJoueur.Value.ScoreTotal} {Pluriel(meilleurJoueur.Value.ScoreTotal, "pt", "pts")})";
        MeilleurButeurText.Text = $"{meilleurButeur.Key} ({meilleurButeur.Value.NbBut} {Pluriel(meilleurButeur.Value.NbBut, "but", "buts")})";
        MeilleurPasseurText.Text = $"{meilleurPasseur.Key} ({meilleurPasseur.Value.NbDecisive} {Pluriel(meilleurPasseur.Value.NbDecisive, "passe", "passes")})";
        MeilleurGardienText.Text = $"{meilleurGardien.Key} ({meilleurGardien.Value.NbArret} {Pluriel(meilleurGardien.Value.NbArret, "arrêt", "arrêts")})";

        // Classement général : rang (#1, #2...) et mise en avant de la première place
        var classement = statistiquesJoueurs.Values
            .OrderByDescending(s => s.ScoreTotal)
            .ToList();

        for (int i = 0; i < classement.Count; i++)
        {
            classement[i].Rang = i + 1;
            classement[i].EstPremier = i == 0;
        }

        ClassementItems.ItemsSource = classement;
    }

    private static string Pluriel(int valeur, string singulier, string pluriel) =>
        valeur > 1 ? pluriel : singulier;

    private void RevenirAccueil_Click(object? sender, RoutedEventArgs e)
    {
        // Retour simple à la page d'accueil sans réinitialiser les données
        MainTabControl.SelectedItem = WelcomeTab;

        ApresAffichage(() => Animer(WelcomePanel, null, 300));
    }
}
