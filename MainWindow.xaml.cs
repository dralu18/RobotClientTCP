using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RobotClientTCP
{
    public sealed partial class MainWindow : Window
    {
        // Clients TCP pour les commandes et la vidéo
        private TcpClient _tcpClient;
        private NetworkStream _stream;
        private bool _isConnected = false;

        private int _modeActuel = 0;
        private int _sousModeActuel = 0;
        private int _etatBouton = 0;

        private TcpClient _tcpVideo;
        private NetworkStream _streamVideo;
        private bool _isVideoConnected = false;
        private const int PORT_VIDEO_OFFSET = 1;

        public MainWindow()
        {
            this.InitializeComponent();

            // Attachement des événements manuels (clic, relâchement)
            RegisterButtonEvents(BtnUp);
            RegisterButtonEvents(BtnDown);
            RegisterButtonEvents(BtnLeft);
            RegisterButtonEvents(BtnRight);
        }

        private void RegisterButtonEvents(Button btn)
        {
            btn.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(BtnDirection_PointerPressed), true);
            btn.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(BtnDirection_PointerReleased), true);
            btn.AddHandler(UIElement.PointerExitedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(BtnDirection_PointerReleased), true);
        }

        // Construction et envoi de la trame de données selon le protocole
        private async Task EnvoyerTrameGlobale()
        {
            if (!_isConnected) return;
            int segmentMode = (_modeActuel * 10) + _sousModeActuel;
            string trame;

            // Format différent si mode manuel (inclut puissance et état bouton)
            if (_modeActuel == 1)
            {
                int segmentPuissance = (int)SliderPower.Value;
                trame = $"{segmentMode};{segmentPuissance};{_etatBouton}";
            }
            else
            {
                trame = $"{segmentMode}";
            }

            await EnvoyerCommandeAsync(trame);
        }

        // Gestion de la connexion aux deux sockets (Commande + Vidéo)
        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected) { Deconnecter(); return; }

            BtnConnect.IsEnabled = false;
            TxtStatus.Text = "Connexion...";
            Log("Tentative de connexion...");

            try
            {
                // Connexion Commande
                _tcpClient = new TcpClient();
                int portCmd = int.TryParse(TxtPort.Text, out int p) ? p : 5050;

                await _tcpClient.ConnectAsync(TxtIp.Text, portCmd);
                _stream = _tcpClient.GetStream();
                _isConnected = true;

                // Connexion Vidéo
                int portVideo = int.TryParse(VideoPort.Text, out int vp) ? vp : 5050;
                _tcpVideo = new TcpClient();
                await _tcpVideo.ConnectAsync(TxtIp.Text, portVideo);
                _streamVideo = _tcpVideo.GetStream();
                _isVideoConnected = true;

                // Démarrage tâches de fond
                _ = RecevoirFluxVideoAsync();
                _ = EcouterReseauAsync();

                UpdateUIState(true);
                Log($"Connecté Commandes:{portCmd} / Vidéo:{portVideo}");

                // Init mode défaut
                _modeActuel = 0;
                _sousModeActuel = 0;
                await EnvoyerTrameGlobale();
            }
            catch (Exception ex)
            {
                Log($"Erreur de connexion : {ex.Message}");
                Deconnecter();
                TxtStatus.Text = "Erreur";
                TxtStatus.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red);
            }
            finally { BtnConnect.IsEnabled = true; }
        }

        // Nettoyage complet des ressources réseau
        private void Deconnecter()
        {
            _isConnected = false;
            _isVideoConnected = false;

            _stream?.Close();
            _tcpClient?.Close();

            _streamVideo?.Close();
            _tcpVideo?.Close();

            if (RbStop != null) RbStop.IsChecked = true;
            UpdateUIState(false);
            Log("Déconnecté.");

            VideoDisplay.Source = null;
        }

        // Boucle de réception et décodage des images
        private async Task RecevoirFluxVideoAsync()
        {
            byte[] sizeBuffer = new byte[4];

            while (_isVideoConnected)
            {
                try
                {
                    // 1. Lire la taille de l'image (4 octets)
                    int bytesRead = await ReadExactAsync(_streamVideo, sizeBuffer, 4);
                    if (bytesRead != 4) break;

                    int imageSize = BitConverter.ToInt32(sizeBuffer, 0);

                    if (imageSize <= 0 || imageSize > 10_000_000) continue;

                    // 2. Lire l'image complète
                    byte[] imageBuffer = new byte[imageSize];
                    bytesRead = await ReadExactAsync(_streamVideo, imageBuffer, imageSize);
                    if (bytesRead != imageSize) break;

                    // 3. Afficher sur le thread UI
                    DispatcherQueue.TryEnqueue(async () =>
                    {
                        try
                        {
                            using (var ms = new MemoryStream(imageBuffer))
                            {
                                var bitmap = new BitmapImage();
                                await bitmap.SetSourceAsync(ms.AsRandomAccessStream());
                                VideoDisplay.Source = bitmap;
                            }
                        }
                        catch { }
                    });
                }
                catch (Exception)
                {
                    break;
                }
            }
        }

        // Helper pour garantir la lecture du nombre exact d'octets
        private async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, int length)
        {
            int totalRead = 0;
            while (totalRead < length)
            {
                int read = await stream.ReadAsync(buffer, totalRead, length - totalRead);
                if (read == 0) return 0;
                totalRead += read;
            }
            return totalRead;
        }

        // Gestion du changement de mode via RadioButtons
        private async void BtnMode_Checked(object sender, RoutedEventArgs e)
        {
            // Gestion visibilité des panneaux
            if (PanelSubColor != null) PanelSubColor.Visibility = Visibility.Collapsed;
            if (PanelSubFigure != null) PanelSubFigure.Visibility = Visibility.Collapsed;

            if (sender == RbColor && PanelSubColor != null) PanelSubColor.Visibility = Visibility.Visible;
            if (sender == RbFigure && PanelSubFigure != null) PanelSubFigure.Visibility = Visibility.Visible;

            if (BtnRecherche != null) BtnRecherche.IsChecked = false;

            _sousModeActuel = 0;

            if (!_isConnected) return;

            // Assignation du mode
            if (sender == RbStop) _modeActuel = 0;
            if (sender == RbManual) _modeActuel = 1;
            if (sender == RbLine) _modeActuel = 2;
            if (sender == RbColor) _modeActuel = 3;
            if (sender == RbFigure) _modeActuel = 4;

            // Activation UI manuel
            bool isManuel = (_modeActuel == 1);
            BtnUp.IsEnabled = isManuel;
            BtnDown.IsEnabled = isManuel;
            BtnLeft.IsEnabled = isManuel;
            BtnRight.IsEnabled = isManuel;
            SliderPower.IsEnabled = isManuel;

            await EnvoyerCommandeAsync(_modeActuel.ToString());
        }

        // Appui bouton direction : Définit le sous-mode et active l'état
        private async void BtnDirection_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_isConnected || _modeActuel != 1) return;

            if (sender is Button btn && btn.Tag is string tag)
            {
                switch (tag)
                {
                    case "AVANCER": _sousModeActuel = 1; break;
                    case "RECULER": _sousModeActuel = 2; break;
                    case "GAUCHE": _sousModeActuel = 3; break;
                    case "DROITE": _sousModeActuel = 4; break;
                }

                _etatBouton = 1;
                await EnvoyerTrameGlobale();
            }
        }

        // Relâchement bouton direction : Désactive l'état
        private async void BtnDirection_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (!_isConnected || _modeActuel != 1) return;

            if (_etatBouton == 0) return;

            _etatBouton = 0;
            await EnvoyerTrameGlobale();
        }

        // Commandes spécifiques (Figures pré-programmées)
        private async void BtnSubCommand_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected) return;

            if (sender is Button btn && btn.Tag is string tag)
            {
                if (tag == "CMD:RECHERCHE") _sousModeActuel = 1;
                if (tag == "CMD:FIG1") _sousModeActuel = 1;
                if (tag == "CMD:FIG2") _sousModeActuel = 2;
                if (tag == "CMD:FIG3") _sousModeActuel = 3;

                await EnvoyerTrameGlobale();
            }
        }

        // Mise à jour affichage slider puissance
        private void SliderPower_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (TxtLabelPowerValue != null)
            {
                TxtLabelPowerValue.Text = $"{(int)e.NewValue}%";
            }
        }

        // Envoi TCP bas niveau
        private async Task EnvoyerCommandeAsync(string cmd)
        {
            if (!_isConnected || _stream == null) return;
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(cmd + "\n");
                await _stream.WriteAsync(data, 0, data.Length);
                Log($"[ENVOI] > {cmd}");
            }
            catch (Exception ex)
            {
                Log($"Erreur envoi: {ex.Message}");
                Deconnecter();
            }
        }

        // Réception TCP bas niveau (Logs/Retours commandes)
        private async Task EcouterReseauAsync()
        {
            byte[] buffer = new byte[1024];
            while (_isConnected)
            {
                try
                {
                    int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) throw new Exception("Serveur coupé");
                    string received = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    DispatcherQueue.TryEnqueue(() =>
                    {
                        Log($"[RECU] < {received.Trim()}");
                    });
                }
                catch
                {
                    break;
                }
            }
        }

        // Toggle spécifique pour le mode recherche
        private async void BtnRecherche_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected) return;

            if (sender is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton toggle)
            {
                string commande = (toggle.IsChecked == true) ? "31" : "30";
                _sousModeActuel = (toggle.IsChecked == true) ? 1 : 0;

                await EnvoyerCommandeAsync(commande);
            }
        }

        // Mise à jour de l'état des contrôles UI (activé/désactivé)
        private void UpdateUIState(bool connected)
        {
            BtnConnect.Content = connected ? "Déconnexion" : "Connexion";
            TxtStatus.Text = connected ? "Connecté" : "Non connecté";
            TxtStatus.Foreground = new SolidColorBrush(connected ? Microsoft.UI.Colors.Green : Microsoft.UI.Colors.Gray);

            TxtIp.IsEnabled = !connected;
            TxtPort.IsEnabled = !connected;

            RbStop.IsEnabled = connected;
            RbManual.IsEnabled = connected;
            RbLine.IsEnabled = connected;
            RbColor.IsEnabled = connected;
            RbFigure.IsEnabled = connected;
            SliderPower.IsEnabled = connected;

            if (!connected)
            {
                PanelSubColor.Visibility = Visibility.Collapsed;
                PanelSubFigure.Visibility = Visibility.Collapsed;
                // Désactivation contrôles manuels
                BtnUp.IsEnabled = false;
                BtnDown.IsEnabled = false;
                BtnLeft.IsEnabled = false;
                BtnRight.IsEnabled = false;
            }
        }

        // Affichage des messages dans la zone de logs avec auto-scroll
        private void Log(string msg)
        {
            if (TxtLogs == null) return;

            TxtLogs.Text += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            ScrollLogs.UpdateLayout();
            ScrollLogs.ChangeView(null, ScrollLogs.ScrollableHeight, null);
        }
    }
}