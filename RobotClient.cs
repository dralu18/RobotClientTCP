using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

public class RobotClient
{
    private TcpClient _client;
    private NetworkStream _stream;

    // Établit la connexion et lance l'écoute
    public async Task Connecter(string ip, int port)
    {
        _client = new TcpClient();
        await _client.ConnectAsync(ip, port);
        _stream = _client.GetStream();

        // Lance l'écoute en arrière-plan sans attendre
        _ = EcouterReponse();
    }

    // Envoie une commande convertie en octets
    public async Task EnvoyerCommande(string commande)
    {
        if (_client != null && _client.Connected)
        {
            byte[] data = Encoding.ASCII.GetBytes(commande);
            await _stream.WriteAsync(data, 0, data.Length);
        }
    }

    // Boucle pour lire les réponses du serveur
    private async Task EcouterReponse()
    {
        byte[] buffer = new byte[256];
        while (_client.Connected)
        {
            try
            {
                // Lecture asynchrone des données
                int bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    string message = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    // Traitement du message ici
                }
            }
            catch { break; } // Sortie de boucle en cas d'erreur
        }
    }
}