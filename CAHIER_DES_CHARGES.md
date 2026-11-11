# ARKADIA — Cahier des charges technique v2 (destiné à l'agent de développement)

> Document de référence unique. L'agent doit le lire en entier avant d'écrire la moindre ligne, puis l'appliquer jalon par jalon (section 20).
> Remplace le cahier des charges initial (Gemini). En cas de contradiction, **ce document prévaut**.

---

## 0. Décisions verrouillées (ne pas remettre en question)

| # | Sujet | Décision |
|---|---|---|
| 1 | Plateforme | Windows 11 minimum (build 22621+), x64 |
| 2 | Runtime | .NET 8 (LTS), WPF, C# 12, `nullable` activé |
| 3 | UI | WPF UI (lepoco/wpfui), thème Fluent, Mica/Acrylic |
| 4 | Pattern | MVVM strict (CommunityToolkit.Mvvm) + injection de dépendances (Microsoft.Extensions.Hosting) |
| 5 | Données locales | SQLite (WAL) dans `%AppData%\Arkadia`, accès via `Microsoft.Data.Sqlite` + Dapper |
| 6 | Secrets | DPAPI (`ProtectedData`, portée `CurrentUser`, entropie propre à l'app) |
| 7 | Droits | **Aucune élévation administrateur**, ni à l'installation, ni à l'exécution |
| 8 | Installateur / MAJ | Velopack (installation par utilisateur, mises à jour automatiques en deltas) |
| 9 | Succès | **Steam uniquement** (GOG, Epic : hors périmètre) |
| 10 | Bibliothèque | Jeux possédés par compte sur chaque launcher quand une voie officielle existe ; sinon jeux installés seulement |
| 11 | Musique | Spotify, Deezer, YouTube Music. **Apple Music annulé** |
| 12 | Hors ligne | Natif : l'app est pleinement utilisable sans internet avec les dernières données en cache |
| 13 | Langues | FR + EN dès le départ via fichiers RESX, changement de langue à chaud |
| 14 | Thèmes | Bleu (défaut), Clair, Noir + personnalisation totale (roue/hex) |
| 15 | Télémétrie | **Opt-in**, anonyme, granulaire, visualisable, point d'envoi configurable |
| 16 | Licence | Open source, **MIT** |
| 17 | Logo | Création originale (aucun élément de Steam, Apple ou Chrome) |
| 18 | Fenêtre | Sans bordure native, redimensionnable, compatible Snap Windows 11 |
| 19 | Tests | Tests unitaires obligatoires (xUnit) |
| 20 | Livraison | Tout le périmètre en une passe, mais construit par jalons avec build + tests verts à chaque jalon |

---

## 1. Vision et périmètre

### 1.1 Vision
ARKADIA est un hub Windows qui regroupe **jeux, succès, musique, son et raccourcis** dans une interface sombre, épurée et futuriste. Principes directeurs :
1. **Zéro friction** : le moins de clics et de configuration possible, assistants guidés, détection automatique.
2. **Autonome** : ne dépend d'aucun serveur à nous. Tout fonctionne en local avec les API publiques/officielles.
3. **Léger et fiable** : faible consommation, aucun bug bloquant, jamais de perte de données.
4. **Sûr** : aucun mot de passe stocké, secrets chiffrés, aucune fuite dans les logs.

### 1.2 Inclus dans la v1
- Accueil en grille de tuiles (3×3 par défaut, fusionnables, personnalisables)
- Bibliothèque de jeux unifiée : Steam, Epic Games (installés), Minecraft/Mojang, CurseForge (modpacks installés), Rinaorc, jeux ajoutés manuellement, scan du PC
- Lancement 1 clic, fermeture, installation/téléchargement via le launcher d'origine
- Suivi du dernier jeu joué et du temps de jeu
- Succès Steam (pourcentage global, détail par jeu, rareté mondiale, dates)
- Lecteur média (play/pause/suivant/précédent, pochette, titre, volume) via GSMTC
- Onglet Sons : mixeur audio détaillé style Windows (et plus)
- Raccourcis personnalisés (lancer applications, fichiers, URLs)
- Paramètres complets, thèmes, localisation
- Démarrage automatique minimisé dans le tray, menu contextuel du tray
- Instance unique
- Hors-ligne natif, sauvegardes automatiques, journaux
- Télémétrie opt-in
- Installateur et mises à jour automatiques

### 1.3 Hors périmètre v1 (ne pas implémenter)
- GOG Galaxy, EA App, Ubisoft Connect, Battle.net, Xbox
- Succès Epic/GOG
- Apple Music
- Actions système (éteindre, redémarrer, verrouiller, scripts)
- Bibliothèque Epic complète non installée (pas d'API officielle)
- Gestion complète de profils/versions Minecraft (on lance le launcher officiel)
- File de téléchargement interne avec progression (on délègue au launcher d'origine)
- Égaliseur audio / effets (nécessite un pilote APO)

> Architecture : chaque launcher est un **plugin** (interface `IGameProvider`) pour pouvoir ajouter GOG et d'autres plus tard sans toucher au cœur.

---

## 2. Environnement de développement

- Windows 11, .NET 8 SDK, VS Code (extensions C# Dev Kit, XAML), Git.
- WPF ne compile que sous Windows : l'agent doit vérifier `dotnet --info` au départ.
- Ne **jamais** exécuter l'app avec élévation pour la tester.
- Le réseau peut être indisponible : si un package n'est pas installable, l'agent le note dans `DECISIONS.md` et choisit une alternative, sans bloquer.
- Fichiers à la racine du dépôt : `README.md`, `LICENSE` (MIT), `CHANGELOG.md`, `DECISIONS.md` (journal des choix de l'agent), `TODO_HUMAN.md` (actions que seul Thomas peut faire : clés API, enregistrement Azure, etc.), `.editorconfig`, `.gitignore`, `Directory.Build.props`, `Directory.Packages.props` (gestion centralisée des versions).

---

## 3. Stack et dépendances

### 3.1 Projet
- `TargetFramework` : `net8.0-windows10.0.22621.0`
- `UseWPF` : true, `Nullable` : enable, `ImplicitUsings` : enable
- `TreatWarningsAsErrors` : true, `AnalysisLevel` : latest-recommended
- Manifest : DPI `PerMonitorV2`, `longPathAware`, `asInvoker` (jamais `requireAdministrator`)
- Publication : `self-contained`, `win-x64`, `ReadyToRun`, `TieredPGO`. **Pas de trimming** (non supporté par WPF).

### 3.2 Paquets NuGet (versions à figer dans `Directory.Packages.props`)
| Besoin | Paquet |
|---|---|
| UI Fluent | `WPF-UI`, `WPF-UI.Tray` (si disponible, sinon `H.NotifyIcon.Wpf`) |
| MVVM | `CommunityToolkit.Mvvm` |
| DI / hosting / config | `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Http` |
| SQLite | `Microsoft.Data.Sqlite`, `Dapper` |
| Résilience HTTP | `Microsoft.Extensions.Http.Resilience` (ou `Polly`) |
| Logs | `Serilog`, `Serilog.Sinks.File`, `Serilog.Extensions.Hosting` |
| Parsing VDF/ACF Steam | `ValveKeyValue` |
| Auth Microsoft (Minecraft) | `Microsoft.Identity.Client` (MSAL) |
| Audio | `NAudio` (WASAPI, sessions audio) |
| Tâche planifiée | `TaskScheduler` (dahall) |
| MAJ / installateur | `Velopack` |
| Tests | `xunit`, `NSubstitute`, `AwesomeAssertions`, `Microsoft.NET.Test.Sdk`, `coverlet.collector` |
| Sélecteur de couleur | Contrôle maison (roue HSV + hex). Option : `PixiEditor.ColorPicker` après vérification de licence |

> Règle : **aucune dépendance non maintenue ou à licence restrictive**. Vérifier la licence de chaque paquet et la consigner dans `DECISIONS.md`.

---

## 4. Architecture de la solution

```
Arkadia.sln
├─ src/
│  ├─ Arkadia.App/                 (WPF : vues, thèmes, ressources, composition root)
│  ├─ Arkadia.Core/                (domaine : modèles, interfaces, services purs, sans dépendance UI)
│  ├─ Arkadia.Infrastructure/      (SQLite, HTTP, DPAPI, registre, fichiers, tâche planifiée, logs)
│  ├─ Arkadia.Providers.Steam/
│  ├─ Arkadia.Providers.Epic/
│  ├─ Arkadia.Providers.Minecraft/ (Mojang, CurseForge, Rinaorc)
│  ├─ Arkadia.Providers.Manual/    (jeux manuels + scan du PC)
│  ├─ Arkadia.Media/               (GSMTC + audio WASAPI)
│  └─ Arkadia.Localization/        (RESX FR/EN + gestionnaire à chaud)
├─ tests/
│  ├─ Arkadia.Core.Tests/
│  ├─ Arkadia.Infrastructure.Tests/
│  ├─ Arkadia.Providers.Tests/     (fixtures : vrais exemples de .acf, .vdf, .item, JSON d'API)
│  └─ Arkadia.App.Tests/           (ViewModels)
├─ assets/ (logo, icônes, polices)
├─ docs/
└─ .github/workflows/ci.yml
```

### 4.1 Règles d'architecture
- `Core` ne référence **rien** d'autre (ni WPF, ni SQLite).
- Les `ViewModel` ne contiennent aucune référence à des contrôles WPF. Navigation via un `INavigationService`.
- Toute I/O est `async` avec `CancellationToken`. Aucun `.Result` / `.Wait()` sur le thread UI.
- Tout accès au registre, au système de fichiers, aux processus, à l'horloge et au réseau passe par une interface (testable).
- Événements entre modules via un `IMessenger` (CommunityToolkit) plutôt que des références croisées.

### 4.2 Interfaces clés (Core)
```csharp
public interface IGameProvider
{
    string Id { get; }                       // "steam", "epic", "minecraft", "curseforge", "rinaorc", "manual"
    ProviderCapabilities Capabilities { get; } // flags : DetectInstalled, OwnedLibrary, Launch, Install, Achievements, PlaytimeSource
    Task<ProviderStatus> GetStatusAsync(CancellationToken ct);
    Task<IReadOnlyList<GameEntry>> ScanInstalledAsync(CancellationToken ct);
    Task<IReadOnlyList<GameEntry>> GetOwnedAsync(CancellationToken ct);   // peut renvoyer vide si non supporté
    Task<LaunchResult> LaunchAsync(GameEntry game, CancellationToken ct);
    Task<bool> InstallAsync(GameEntry game, CancellationToken ct);        // délègue au launcher
    Task<TimeSpan?> GetPlaytimeAsync(GameEntry game, CancellationToken ct);
    Task<DateTimeOffset?> GetLastPlayedAsync(GameEntry game, CancellationToken ct);
}

public interface IAchievementProvider { /* Steam uniquement en v1 */ }
public interface IMediaController { /* GSMTC */ }
public interface IAudioMixer { /* sessions, périphériques, volumes */ }
public interface ISecretStore { Task SetAsync(string key, string value); Task<string?> GetAsync(string key); Task RemoveAsync(string key); }
public interface ISettingsService { /* lecture/écriture typée, événements de changement */ }
public interface ITelemetryService { /* opt-in, file locale, envoi par lots */ }
public interface IClock, IFileSystem, IRegistry, IProcessService, IConnectivityService
```

---

## 5. Données locales et mode hors ligne

### 5.1 Emplacement
```
%AppData%\Arkadia\
├─ arkadia.db            (SQLite WAL)
├─ backups\              (7 dernières sauvegardes quotidiennes)
├─ cache\images\         (pochettes, icônes, avatars)
├─ logs\                 (Serilog, rotation)
├─ secrets.bin           (blob DPAPI)
├─ settings.json         (préférences non sensibles, écriture atomique)
└─ crash\                (rapports de crash locaux)
```

### 5.2 Robustesse (zéro perte de données)
- SQLite : `PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;`
- Migrations versionnées et idempotentes (table `schema_version`), exécutées dans une transaction. Une migration échouée annule tout et restaure la sauvegarde.
- Au démarrage : `PRAGMA quick_check`. Si corruption → restauration automatique de la dernière sauvegarde valide + notification utilisateur + journal.
- Sauvegarde quotidienne via l'API de backup SQLite (`VACUUM INTO`), conservation de 7 fichiers.
- Fichiers JSON : écriture atomique (fichier temporaire + `File.Replace`).
- Toutes les écritures groupées en transactions courtes. Jamais de transaction ouverte pendant un appel réseau.

### 5.3 Schéma (version 1)
```sql
schema_version(version INTEGER NOT NULL, applied_utc TEXT NOT NULL)

accounts(provider TEXT PRIMARY KEY, display_name TEXT, external_id TEXT, avatar_path TEXT,
         status TEXT NOT NULL, last_sync_utc TEXT)

games(id INTEGER PRIMARY KEY, provider TEXT NOT NULL, external_id TEXT, title TEXT NOT NULL,
      sort_title TEXT, install_path TEXT, exe_path TEXT, launch_uri TEXT,
      is_installed INTEGER NOT NULL, is_owned INTEGER NOT NULL, is_manual INTEGER NOT NULL DEFAULT 0,
      is_hidden INTEGER NOT NULL DEFAULT 0, is_favorite INTEGER NOT NULL DEFAULT 0,
      cover_path TEXT, header_path TEXT, added_utc TEXT NOT NULL, last_seen_utc TEXT,
      last_played_utc TEXT, total_playtime_sec INTEGER NOT NULL DEFAULT 0,
      UNIQUE(provider, external_id))

game_sessions(id INTEGER PRIMARY KEY, game_id INTEGER NOT NULL REFERENCES games(id) ON DELETE CASCADE,
              start_utc TEXT NOT NULL, end_utc TEXT, source TEXT NOT NULL)  -- 'arkadia' | 'provider'

achievement_games(app_id INTEGER PRIMARY KEY, game_id INTEGER REFERENCES games(id), total INTEGER,
                  unlocked INTEGER, last_sync_utc TEXT, has_achievements INTEGER NOT NULL)

achievements(app_id INTEGER NOT NULL, api_name TEXT NOT NULL, display_name TEXT, description TEXT,
             icon_path TEXT, icon_gray_path TEXT, hidden INTEGER, unlocked INTEGER NOT NULL,
             unlock_utc TEXT, global_percent REAL, PRIMARY KEY(app_id, api_name))

tiles(id TEXT PRIMARY KEY, type TEXT NOT NULL, col INTEGER, row INTEGER,
      col_span INTEGER NOT NULL DEFAULT 1, row_span INTEGER NOT NULL DEFAULT 1,
      is_visible INTEGER NOT NULL DEFAULT 1, config_json TEXT)

shortcuts(id INTEGER PRIMARY KEY, name TEXT NOT NULL, target TEXT NOT NULL, arguments TEXT,
          working_dir TEXT, icon_path TEXT, sort_order INTEGER NOT NULL)

settings(key TEXT PRIMARY KEY, value TEXT NOT NULL)

telemetry_queue(id INTEGER PRIMARY KEY, created_utc TEXT NOT NULL, event TEXT NOT NULL, payload_json TEXT NOT NULL)

http_cache(key TEXT PRIMARY KEY, etag TEXT, fetched_utc TEXT NOT NULL, expires_utc TEXT, body BLOB)
```
Index : `games(provider)`, `games(last_played_utc DESC)`, `games(title COLLATE NOCASE)`, `achievements(app_id, unlocked)`.

### 5.4 Mode hors ligne natif
- `IConnectivityService` : `NetworkChange` + sonde HTTP légère (timeout court). État exposé : `Online | Offline | Limited`.
- **Stale-while-revalidate** : l'UI affiche toujours d'abord les données locales, puis se rafraîchit en arrière-plan si en ligne.
- Chaque donnée distante est stockée avec sa date de dernière synchronisation (affichée discrètement : « Mis à jour il y a 3 h »).
- Pochettes et icônes mises en cache disque (`cache\images`), clé = hash de l'URL, plafond configurable (500 Mo par défaut) avec éviction LRU.
- Un bandeau discret « Hors ligne » apparaît ; les boutons qui exigent internet sont désactivés avec une info-bulle claire (pas de message d'erreur brutal).
- Les actions en échec réseau réessaient avec backoff exponentiel (Polly), sans boucle infinie ; erreurs 429 respectent `Retry-After`.

---

## 6. Sécurité

### 6.1 Principes
- **Aucun mot de passe n'est jamais demandé ni stocké par ARKADIA.**
- Seuls des jetons, identifiants publics et clés API sont conservés, **chiffrés** avec DPAPI.
- HTTPS uniquement (TLS 1.2+), validation de certificat par défaut, aucun contournement.
- Aucun secret en clair en mémoire plus longtemps que nécessaire ; tableaux d'octets effacés après usage (`CryptographicOperations.ZeroMemory`).

### 6.2 Implémentation `ISecretStore`
- `ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser)`.
- `entropy` = valeur constante propre à l'app (dérivée d'un identifiant fixe + hash de la version majeure de format) ; ne jamais logguer.
- Stockage : un fichier `secrets.bin` contenant un dictionnaire sérialisé puis chiffré globalement (écriture atomique), ou une ligne chiffrée par clé en base. Choisir l'option la plus simple et la documenter.
- Si le déchiffrement échoue (autre session Windows, profil restauré) : supprimer proprement, marquer le compte « à reconnecter », ne pas planter.

### 6.3 Journaux et télémétrie
- Filtre de masquage dans Serilog : toute valeur qui ressemble à une clé API (32 caractères hexadécimaux), un jeton Bearer, un SteamID privé ou un chemin contenant le nom d'utilisateur Windows est masquée.
- Aucun chemin complet ni nom de jeu dans la télémétrie (sauf si l'utilisateur l'active explicitement, voir 15).

### 6.4 Surface d'attaque
- Les URI de lancement sont **construites** par le code à partir d'identifiants validés (entiers/GUID), jamais concaténées depuis une saisie libre. Liste blanche de schémas : `steam://`, `com.epicgames.launcher://`, `spotify:`, `deezer://`, `https://`, `file` pour les raccourcis utilisateur.
- Raccourcis personnalisés : confirmation à la première exécution d'une cible inconnue, avertissement pour les scripts (`.bat`, `.ps1`, `.cmd`, `.vbs`).
- `Process.Start` avec `UseShellExecute` seulement quand nécessaire ; jamais de commande shell construite par concaténation.
- Écouteur de rappel OAuth : `HttpListener` sur `127.0.0.1` uniquement, port éphémère, fermeture immédiate après réception, vérification du paramètre `state`.
- Pas de WebView embarquée pour les connexions : navigateur système.

---

## 7. Comptes et connexions (SSO réaliste)

Principe : **OAuth/OpenID public quand il existe, sinon API publique avec clé utilisateur, sinon détection locale.**

| Service | Méthode | Détail |
|---|---|---|
| Steam | OpenID 2.0 (public) + clé Web API | Identité via OpenID, puis données via la Web API |
| Minecraft / Mojang | OAuth Microsoft (MSAL) | Voir 7.2 (dépend d'une approbation Mojang) |
| Epic Games | Détection locale uniquement | Aucun OAuth public pour un launcher tiers |
| CurseForge | Clé API CurseForge + lecture locale | Voir 7.3 |
| Rinaorc | Raccourci vers son launcher | Voir 7.4 |
| Spotify / Deezer / YouTube Music | Aucun compte : GSMTC (session Windows) | Pas de connexion requise |

### 7.1 Steam
1. **Connexion OpenID** : ouvrir `https://steamcommunity.com/openid/login` avec `openid.mode=checkid_setup`, `openid.ns=http://specs.openid.net/auth/2.0`, `openid.identity`/`claimed_id` = `http://specs.openid.net/auth/2.0/identifier_select`, `openid.return_to` = `http://127.0.0.1:{port}/steam/callback`, `openid.realm` = `http://127.0.0.1:{port}`.
2. Récupérer la réponse sur le `HttpListener`, **valider** en renvoyant les paramètres à Steam avec `openid.mode=check_authentication` (réponse `is_valid:true`). Extraire le **SteamID64** de `openid.claimed_id`.
3. **Clé Web API** : assistant guidé au premier lancement (lien vers `https://steamcommunity.com/dev/apikey`, copier/coller, validation par un appel de test). La clé est stockée via `ISecretStore`.
4. **Détection du compte sans connexion** : si Steam est installé, lire le SteamID du compte connecté dans `HKCU\Software\Valve\Steam\ActiveProcess\ActiveUser` (compte Steam 32 bits → convertir en SteamID64) ou `loginusers.vdf` (`MostRecent=1`). Proposer « Utiliser le compte Steam détecté » en un clic : c'est le parcours le plus simple.
5. **Profil privé** : si les jeux/succès renvoient vide, détecter l'état de confidentialité et afficher un message clair avec le lien vers les paramètres de confidentialité Steam.

### 7.2 Minecraft / Mojang
- Authentification Microsoft via MSAL (client public), flux **navigateur système avec redirection loopback**, repli **device code**.
- Chaîne : Microsoft → Xbox Live → XSTS → services Minecraft. Vérification de possession : `https://api.minecraftservices.com/entitlements/mcstore` et profil `https://api.minecraftservices.com/minecraft/profile`.
- **Contrainte** : l'utilisation de l'API Minecraft par une application tierce nécessite un identifiant d'application Azure approuvé par Mojang. Cela relève de Thomas (voir `TODO_HUMAN.md`).
- **Mode dégradé obligatoire** : sans identifiant approuvé, ARKADIA détecte seulement l'installation du launcher officiel et le lance. L'agent doit implémenter les deux modes derrière un drapeau de configuration `Minecraft:OnlineAuthEnabled`.
- Cache de jetons MSAL sérialisé et chiffré via DPAPI (callback de cache personnalisé).

### 7.3 CurseForge
- Clé API (en-tête `x-api-key`) obtenue sur la console CurseForge (voir `TODO_HUMAN.md`). Saisie guidée dans les paramètres, stockée via `ISecretStore`.
- Périmètre v1 : **modpacks installés uniquement**. Source principale : lecture locale des instances de l'application CurseForge (dossier d'instances, fichier `minecraftinstance.json` par instance), enrichie via l'API (nom, logo) quand la clé est présente.
- Chemin de l'application : détecter via le registre / emplacements usuels, et permettre de choisir le dossier manuellement.
- Lancement : lancer l'application CurseForge (ou l'instance si une méthode fiable existe, à vérifier). Aucune réimplémentation d'un launcher Minecraft.
- Respecter les conditions d'utilisation de l'API CurseForge (attribution, pas de redistribution de mods).

### 7.4 Rinaorc
- Hypothèse (à confirmer par Thomas dans `TODO_HUMAN.md`) : Rinaorc = serveur Minecraft avec son propre launcher.
- Implémentation v1 : fournisseur « raccourci » : détection du launcher Rinaorc dans les emplacements usuels et le registre, sinon chemin choisi par l'utilisateur, lancement 1 clic, icône personnalisable.
- Ne pas supposer d'API ; si Rinaorc propose un protocole d'URI, l'ajouter en option.

### 7.5 Epic Games
- **Installés uniquement** (décision verrouillée). Lecture de `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item` (JSON : `AppName`, `DisplayName`, `InstallLocation`, `LaunchExecutable`, `CatalogNamespace`, `CatalogItemId`).
- Lancement : `com.epicgames.launcher://apps/{AppName}?action=launch&silent=true` (à vérifier), repli sur l'exécutable direct.
- Pas de succès, pas de bibliothèque non installée. L'interface indique « Bibliothèque Epic complète non disponible » dans l'onglet Comptes, sans ton d'erreur.

---

## 8. Moteur de jeux

### 8.1 Détection (scan)
- **Premier lancement** : scan automatique en tâche de fond (non bloquant, barre de progression discrète, annulable). Les résultats apparaissent au fur et à mesure.
- **Ensuite** : scan à la demande (bouton « Rechercher des jeux ») + rafraîchissement léger à l'ouverture de l'app et surveillance (`FileSystemWatcher`) des dossiers de manifestes Steam/Epic, avec anti-rebond.
- Priorité à des **sources fiables** (manifestes des launchers), puis scan générique.

**Steam**
- Dossier Steam : `HKCU\Software\Valve\Steam\SteamPath` (repli : `HKLM\SOFTWARE\WOW6432Node\Valve\Steam\InstallPath`).
- Bibliothèques : `steamapps\libraryfolders.vdf` (plusieurs disques).
- Jeux installés : `steamapps\appmanifest_*.acf` (`appid`, `name`, `installdir`, `StateFlags`, `LastUpdated`, `SizeOnDisk`). Ignorer les outils/redistribuables connus (Steamworks Common Redistributables, Proton non applicable sous Windows).
- Jeux possédés : `IPlayerService/GetOwnedGames` avec `include_appinfo=1&include_played_free_games=1`.
- Dernier lancement / temps de jeu : `GetOwnedGames` (`playtime_forever`, `rtime_last_played`) et `GetRecentlyPlayedGames`.
- Visuels : `https://cdn.cloudflare.steamstatic.com/steam/apps/{appid}/library_600x900.jpg` et `header.jpg` (repli si absent) mis en cache disque.

**Scan générique du PC**
- Scan configurable : dossiers choisis par l'utilisateur ou tous les lecteurs fixes (`DriveInfo`), en excluant `Windows`, `Program Files\WindowsApps`, `$Recycle.Bin`, etc.
- Heuristiques : exécutables dans des dossiers contenant des marqueurs de jeux (`UnityPlayer.dll`, `UnrealEngine`, `*.pak`, `steam_api64.dll`, etc.), exclusion des installateurs/désinstalleurs.
- Les candidats sont proposés dans un écran de validation (cocher ce qu'on ajoute), jamais ajoutés silencieusement.

**Ajout manuel** : choisir un `.exe` ou un raccourci, nom, jaquette (image locale ou recherche optionnelle), arguments, répertoire de travail.

### 8.2 Lancement et fermeture
- Priorité : protocole du launcher (`steam://rungameid/{appid}`, `com.epicgames.launcher://…`), repli exécutable direct.
- **Fermer un jeu** : `CloseMainWindow()` → attente (5 s) → proposition « Forcer la fermeture » (arbre de processus). Jamais de kill silencieux.
- Gestion d'erreurs lisibles : launcher non installé, jeu déplacé/supprimé (proposer « Localiser »), disque absent.

### 8.3 Suivi du dernier jeu et du temps de jeu
- Source 1 : données du launcher (Steam) quand disponibles.
- Source 2 : **suivi interne** `PlaytimeTracker` pour les autres : sondage léger toutes les 5–10 s des processus dont le chemin de l'exécutable correspond à un jeu connu (comparaison chemin, pas de requête WMI lourde, pas d'admin). Sessions enregistrées dans `game_sessions`.
- Quand ARKADIA lance un jeu lui-même, il enregistre directement la session et surveille le processus principal et ses enfants.
- « Dernier jeu joué » = max(`last_played_utc`).

### 8.4 Installation / téléchargement
- Bouton « Installer » sur un jeu possédé non installé : ouvre `steam://install/{appid}` (Steam). Aucune barre de progression interne en v1.
- Après action : rafraîchissement du statut quand le manifeste apparaît (watcher).

### 8.5 Écran « Jeux »
- Vue grille de jaquettes + vue liste, tri (récents, nom, temps de jeu), filtres (launcher, installés, favoris, masqués), recherche instantanée (debounce 150 ms), virtualisation obligatoire (`VirtualizingPanel`) pour 1000+ jeux.
- Fiche jeu : visuel, boutons JOUER / FERMER / INSTALLER, temps de jeu, dernière session, succès (Steam), actions (favori, masquer, ouvrir le dossier, modifier).

---

## 9. Succès (Steam uniquement)

- Endpoints Web API : `ISteamUserStats/GetPlayerAchievements` (`appid`, `steamid`, `l={langue}`), `ISteamUserStats/GetSchemaForGame`, `ISteamUserStats/GetGlobalAchievementPercentagesForApp`. Vérifier les paramètres exacts à l'implémentation.
- Stratégie de synchronisation : liste des jeux possédés → pour chaque jeu, synchronisation **à la demande et progressive** (jeux récents d'abord), file avec concurrence limitée (2–3 requêtes) et respect du quota ; état « jamais synchronisé » géré.
- Données stockées : voir tables `achievement_games` et `achievements`.
- **Page Succès** :
  - En-tête : pourcentage de complétion global (succès débloqués / total sur tous les jeux synchronisés), nombre de jeux 100 %.
  - Liste de cartes par jeu : jaquette, barre de progression, `x / y`, tri/filtre.
  - Détail : succès débloqués/verrouillés, icône, description (masquée pour les succès cachés tant que verrouillés), **rareté mondiale (%)** avec étiquette de rareté (commun/rare/ultra-rare selon seuils), **date de déblocage** localisée.
- Tuile Accueil « Dernier succès débloqué » : le plus récent par `unlock_utc`.
- Jeux sans succès : affichage propre « Aucun succès pour ce jeu ».
- Hors-ligne : tout vient du cache.

---

## 10. Musique et audio

### 10.1 Lecteur média (GSMTC)
- API : `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager`.
- Métadonnées : titre, artiste, album, pochette (flux `Thumbnail` → `BitmapImage` mise en cache), position / durée (`GetTimelineProperties`).
- Commandes : `TryPlayAsync`, `TryPauseAsync`, `TryTogglePlayPauseAsync`, `TrySkipNextAsync`, `TrySkipPreviousAsync`, `TryChangePlaybackPositionAsync`.
- **Choix de la source** : Spotify, Deezer, YouTube Music. Modal au premier lancement + bouton engrenage du widget pour changer. La source sert à **filtrer la session GSMTC** à afficher via son `SourceAppUserModelId` (Spotify.exe, Deezer, navigateur pour YouTube Music). Si plusieurs sessions : prendre celle de la source choisie, sinon la session « courante » Windows.
- YouTube Music (navigateur/PWA) : GSMTC remonte la session du navigateur ; documenter les limites (identification par titre de fenêtre/AUMID du navigateur).
- Si l'application de la source n'est pas lancée : afficher « Lancer Spotify » (bouton) plutôt qu'une erreur.
- **Playlists** : bouton « Gérer les playlists » → ouvre l'application native (`spotify:` ; `deezer://www.deezer.com/` ; `https://music.youtube.com/`). Aucune gestion de playlist dans ARKADIA.
- Volume : curseur du widget lié au volume de la **session audio de l'application source** (voir 10.2), pas au volume maître.
- Mises à jour en temps réel par événements (`MediaPropertiesChanged`, `PlaybackInfoChanged`, `TimelinePropertiesChanged`), aucun polling.

### 10.2 Onglet « Sons » (mixeur détaillé)
Niveau visé : au moins l'équivalent du mélangeur de volume Windows, avec plus de détails.

Obligatoire (v1) :
- Liste des **périphériques de sortie et d'entrée** (nom, état, volume maître, mute, périphérique par défaut), via `MMDeviceEnumerator` (NAudio.Wasapi) avec notifications de changement (branchement/débranchement).
- **Sessions audio par application** (`AudioSessionManager`) : nom, icône, volume (`SimpleAudioVolume`), mute, **indicateur de niveau en temps réel** (`AudioMeterInformation`, ~30 fps seulement quand l'onglet est visible).
- Définir le périphérique par défaut (sortie/entrée) — **expérimental** (API non documentée `IPolicyConfig`), derrière un drapeau `Audio:EnableDefaultDeviceSwitch`, avec repli propre en cas d'échec.
- Profils : sauvegarder/restaurer un ensemble de volumes (ex. « Jeu », « Nuit »).

Optionnel (activer seulement si stable, sinon `TODO_HUMAN.md`) :
- Routage de sortie par application (API non documentée, fragile).

Hors périmètre : égaliseur, effets.

Règles : aucun rafraîchissement lorsque l'onglet est masqué ; libération systématique des objets COM ; aucune fuite de handles.

---

## 11. Système

### 11.1 Démarrage automatique
- Paramètre « Lancer au démarrage de Windows » (défaut : activé après confirmation au premier lancement).
- Implémentation : **tâche planifiée** utilisateur (déclencheur : ouverture de session de l'utilisateur courant, niveau d'exécution limité, **retard de 15 s**, argument `--minimized`). Sans droits administrateur.
- Repli automatique : clé `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` si la tâche planifiée est impossible.
- Désactiver = supprimer la tâche **et** la clé. Vérifier l'état réel à l'affichage du paramètre (ne pas se fier à une valeur stockée).
- Compatibilité Velopack : le chemin cible doit pointer le lanceur stable (pas un dossier de version).

### 11.2 System tray
- Icône dans la zone de notification, **démarrage minimisé dans le tray** avec `--minimized`.
- Fermer la fenêtre = **réduire dans le tray** par défaut (paramètre : « Quitter à la fermeture »).
- Menu contextuel : **Jouer** (dernier jeu), **Ouvrir ARKADIA**, **Quitter** ; double-clic = ouvrir.
- Au premier passage en tray, afficher une notification discrète expliquant le comportement (une seule fois).
- Quand l'app est masquée : suspendre les animations, les compteurs de stats, le mixeur ; vider le working set (`GC.Collect` + `EmptyWorkingSet` après un court délai) pour minimiser la RAM.

### 11.3 Instance unique
- `Mutex` nommé (portée session utilisateur) + canal de signalement (named pipe ou `EventWaitHandle`) : un second lancement ramène la fenêtre existante au premier plan (ou la restaure depuis le tray) et transmet les arguments.

### 11.4 Statistiques système (tuile Accueil, version simplifiée)
- Sans admin, indépendant du fabricant : compteurs de performance Windows.
  - CPU : `\Processor Information(_Total)\% Processor Utility`.
  - RAM : `GlobalMemoryStatusEx`.
  - GPU : `\GPU Engine(*)\Utilization Percentage` (somme par moteur 3D) et `\GPU Adapter Memory(*)\Dedicated Usage` ; si indisponible, afficher « — » sans erreur.
  - Températures : **hors périmètre** (nécessitent des pilotes/admin).
- Rafraîchissement 1 s tant que visible, 0 quand masqué.
- Tuile : anneaux/jauges stylisés sci-fi, valeurs en direct, bouton « Détails ».
- « Détails » : ouvre une page/panneau avec historique court (60 s), détail par cœur, RAM utilisée/disponible, et par GPU. **Note** : la demande évoque « le truc du jeu par défaut » (superposition/Game Bar). Comportement : bouton qui ouvre la vue détaillée interne ; en option, lancement de la Xbox Game Bar (`ms-gamebar:`) si disponible. Documenter l'interprétation dans `DECISIONS.md`.

---

## 12. Interface et design

### 12.1 Fenêtre
- `FluentWindow` (WPF UI), **sans barre de titre native**, barre de titre personnalisée (widget média + boutons Réduire/Agrandir/Fermer stylisés).
- Redimensionnable sur tous les bords, **support Snap Layouts de Windows 11** (survol du bouton Agrandir), taille minimale 960×600, défaut 1280×800, état mémorisé (position, taille, écran, maximisé), correctement restauré en multi-écran/DPI variable.
- Fond Mica (repli Acrylic, repli couleur unie si désactivé par Windows ou « effets de transparence » éteints).

### 12.2 Navigation
Barre latérale gauche, repliable (icônes seules / icônes + libellés) :
1. Accueil (Home)
2. Jeux (manette)
3. Succès (trophée)
4. Sons (curseurs)
5. Raccourcis (éclair)
6. *(en bas)* Paramètres (engrenage)

Navigation par `NavigationView` WPF UI, transitions fade/slide (150–250 ms), état sélectionné lumineux, raccourcis clavier (Ctrl+1…5).

### 12.3 Design tokens (par défaut, thème « Bleu »)
```
Accent principal      #00D2FF   (hover plus clair, pressed plus sombre)
Accent secondaire     #3A86FF
Fond fenêtre          #12141D
Surface / carte       #1E202D  (avec transparence 60–85 % sur Mica)
Texte principal       #F2F5FA
Texte secondaire      #9AA3B5
Succès / Erreur / Avertissement : #2ECC71 / #FF5C6C / #FFB020
Rayon des coins       carte 14 px, bouton 10 px, champ 8 px
Ombre / lueur         lueur néon subtile de l'accent au survol (DropShadow/Glow, opacité ≤ 0.35)
Police                Segoe UI Variable (système) ; titres Display en semi-bold
Espacement            grille de 4 px, marges de 16–24 px
```
Tous les tokens sont des **ressources dynamiques** (pas de couleurs codées en dur dans les vues).

### 12.4 Thèmes et personnalisation
- Trois thèmes de base : **Bleu** (sombre, défaut), **Clair** (très clair), **Noir** (noir profond OLED).
- **Personnalisation totale** : menu avec **roue de couleur HSV + saisie hexadécimale** pour : accent principal, accent secondaire, fond, surface, texte. Aperçu en direct. Boutons « Réinitialiser » (par couleur et global), « Exporter / Importer le thème » (JSON).
- Validation de lisibilité : calcul du ratio de contraste (WCAG) ; avertissement non bloquant si < 4,5:1 pour le texte.
- Intensité des effets : curseurs « Transparence/Mica », « Lueur », « Animations » (Complètes / Réduites / Aucune). Respecter `SystemParameters.ClientAreaAnimation`.

### 12.5 Accueil : grille de tuiles
- Grille 3×3 par défaut, `TileGridPanel` personnalisé.
- Tuiles par défaut (ordre) : Dernier jeu joué (bouton **JOUER**), Steam Hub, Epic Games Hub, CurseForge / Mods, Minecraft Launcher, Lecteur Musique principal, Dernier Succès, Statistiques Système, Raccourcis personnalisés.
- **Fusionnables** : une tuile peut occuper 1–3 colonnes × 1–3 lignes ; **redimensionnement** via menu contextuel et poignées en mode édition ; **glisser-déposer** pour réorganiser ; algorithme de placement « premier emplacement libre » avec résolution de collisions et compactage.
- **Mode édition** (bouton « Personnaliser ») : afficher/masquer des tuiles, changer la taille, réorganiser, restaurer la disposition par défaut.
- Disposition persistée dans `tiles`. Une grille corrompue retombe sur la disposition par défaut.
- Tuiles Hub (Steam/Epic/CurseForge/Minecraft) : état du compte/launcher, derniers jeux, bouton d'ouverture du launcher, état « non connecté » avec CTA clair (« Connecter Steam »).
- États vides soignés partout (illustrations légères + action).

### 12.6 Détails d'ergonomie
- Skeleton loaders (pas de spinners plein écran), micro-interactions au survol (élévation + lueur), animations GPU uniquement (`Opacity`, `TranslateTransform`, `ScaleTransform`).
- Navigation clavier complète, focus visible, `AutomationProperties.Name` sur tous les contrôles interactifs.
- Aucun gel de l'UI : tout traitement > 16 ms hors thread UI.

### 12.7 Logo et identité
- Création **originale** : symbole vectoriel néon bleu ciel — un cercle segmenté (type diaphragme) dont les segments dessinent un « A » stylisé, avec un point de lumière. **Interdit** : toute reprise de logos existants (Steam, Apple, Chrome).
- Livrer : `logo.svg`, version XAML (`DrawingImage`), `app.ico` multi-tailles (16, 24, 32, 48, 64, 128, 256), variantes claire/sombre, icône de tray monochrome adaptative (clair/sombre).
- Générer par script reproductible (documenté) ; vérifier le rendu à 16 px.

---

## 13. Localisation (FR + EN)

- Fichiers `Strings.resx` (défaut EN) et `Strings.fr.resx` dans `Arkadia.Localization`. Clés hiérarchiques : `Home.Tile.LastGame.Title`.
- **Aucun texte visible en dur** dans le XAML ou le C#. Extension de balisage `{loc:Loc Key}` avec **changement de langue à chaud** (sans redémarrage).
- Langue par défaut = langue de Windows si supportée, sinon EN. Réglable dans les paramètres.
- Formats : dates, nombres, durées localisés (`CultureInfo`). Pluriels gérés (formes séparées).
- Test automatique : **parité des clés FR/EN**, absence de clés inutilisées, absence de chaînes vides.
- Les messages d'erreur utilisateur sont localisés ; les logs restent en anglais.

---

## 14. Paramètres

Sections :
1. **Général** : langue, démarrage avec Windows, démarrer minimisé, quitter à la fermeture (vs réduire), instance unique.
2. **Apparence** : thème, personnalisation couleurs, effets, animations, taille de la barre latérale.
3. **Comptes & services** : état par service (Steam, Minecraft, CurseForge, Rinaorc, Epic détecté), connecter/déconnecter, saisie guidée des clés API, test de connexion.
4. **Jeux** : dossiers à scanner, lancer un scan, exclusions, comportement de fermeture.
5. **Média & Sons** : source musicale, comportement du widget, profils audio.
6. **Données & confidentialité** : emplacement des données, taille du cache, vider le cache, sauvegarder/restaurer, **exporter mes données**, **supprimer toutes mes données**, télémétrie (voir 15).
7. **À propos** : version, licences tierces, vérifier les mises à jour, ouvrir le dossier des logs, copier les infos de diagnostic.

Tout paramètre est appliqué **immédiatement** (pas de bouton « Appliquer » sauf cas justifié) et persisté.

---

## 15. Télémétrie (opt-in)

- **Désactivée par défaut**. Au premier lancement : écran clair, ton non culpabilisant, choix granulaire :
  1. Rapports de plantage anonymes
  2. Statistiques d'usage (quelles fonctionnalités, fréquences)
  3. Performances (temps de démarrage, RAM, latences)
  4. *(Optionnel, séparé)* Données de bibliothèque (noms de jeux) — **désactivé et masqué sauf activation explicite**
- Identifiant d'installation = GUID aléatoire, **réinitialisable** ; aucune donnée personnelle (pas de nom, de chemin, de SteamID, d'IP stockée côté ARKADIA).
- Fonctionnement : événements écrits dans `telemetry_queue` (local d'abord), envoi par lots (HTTPS POST JSON, compression), jamais bloquant, purge après envoi, plafond de taille.
- **Transparence** : écran « Voir ce qui est envoyé » listant les événements en attente et envoyés ; bouton « Tout supprimer ».
- **Point d'envoi configurable** : `Telemetry:Endpoint` (vide par défaut = tout reste local). L'implémentation d'un petit serveur de collecte auto-hébergé est **hors périmètre** de ce dépôt (mais documenter le schéma d'événement dans `docs/telemetry.md`).
- Respect immédiat du retrait du consentement : arrêt de la collecte et purge de la file.
- Rapport de plantage local toujours écrit dans `crash\` (jamais envoyé sans consentement).

---

## 16. Journalisation et gestion des erreurs

- Serilog → `%AppData%\Arkadia\logs\arkadia-.log`, rotation quotidienne, 14 jours, plafond total 50 Mo, niveau `Information` (configurable : Debug dans Paramètres → À propos).
- Enrichissement : version, build, version Windows, culture. **Masquage des secrets** (voir 6.3).
- Gestionnaires globaux : `Application.DispatcherUnhandledException`, `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`. Comportement : log complet + rapport de crash local + boîte de dialogue claire (« ARKADIA a rencontré un problème », options : Redémarrer / Copier les détails / Quitter). Les erreurs récupérables ne ferment pas l'app.
- Principe « dégradation gracieuse » : l'échec d'un provider/tuile ne doit jamais empêcher le reste de fonctionner (isolation par tuile et par provider avec `try/catch` au bord + état d'erreur visible dans la tuile).
- Aucun `catch` vide ; aucune exception avalée sans log.

---

## 17. Tests et qualité

### 17.1 Tests unitaires obligatoires
- **Parsers** : `libraryfolders.vdf`, `appmanifest_*.acf`, `loginusers.vdf`, manifestes Epic `.item`, `minecraftinstance.json`, réponses JSON des API (fixtures réalistes dans `tests/Fixtures`, y compris cas dégénérés : champs absents, Unicode, chemins longs).
- **Providers** : `HttpMessageHandler` simulé (succès, 401, 403, 429 avec `Retry-After`, 5xx, timeout, JSON invalide).
- **Base de données** : migrations sur fichier temporaire, restauration après corruption simulée, concurrence lecture/écriture.
- **Sécurité** : aller-retour DPAPI, masquage des secrets dans les logs, validation OpenID (`check_authentication` simulé), validation du `state` OAuth.
- **Tuiles** : algorithme de placement/collision/compactage (tests paramétrés, y compris cas limites).
- **Localisation** : parité des clés FR/EN.
- **ViewModels** : états de chargement, hors-ligne, erreur, vide.
- **Hors ligne** : transitions `Online/Offline` et affichage depuis le cache.

### 17.2 Qualité
- Couverture visée : ≥ 80 % sur `Core` et `Providers`, ≥ 60 % global (indicatif, la qualité prime).
- Analyseurs .NET + `TreatWarningsAsErrors`. `dotnet format` vérifié en CI.
- Revue automatique des dépendances (`dotnet list package --vulnerable`).

### 17.3 CI (GitHub Actions, `windows-latest`)
- Étapes : restore → build Release → tests (+ couverture) → vérification de format → audit des vulnérabilités → publication → `vpk pack` sur tag `v*`.
- Le workflow doit exister même si le dépôt n'est pas encore poussé.

---

## 18. Packaging, signature et mises à jour

- **Velopack** : installation par utilisateur dans `%LocalAppData%\Arkadia`, **sans UAC**, raccourcis Démarrer, désinstallation propre (option : conserver ou supprimer `%AppData%\Arkadia`).
- Build : `dotnet publish -c Release -r win-x64 --self-contained -p:PublishReadyToRun=true` puis `vpk pack`.
- Mises à jour automatiques : vérification au démarrage + toutes les 6 h, téléchargement en arrière-plan (deltas), application au prochain redémarrage (ou à la fermeture) ; notification discrète « Mise à jour prête ». Source configurable (`Updates:Source` : dépôt GitHub Releases par défaut). Canal `stable` (et `beta` prévu).
- **Signature** : prévoir l'intégration (variables CI) ; documenter la candidature à SignPath Foundation (open source) dans `docs/release.md`. Sans signature, SmartScreen affichera un avertissement : le documenter dans le README.
- Intégrité : publier les empreintes SHA-256 avec chaque release.

---

## 19. Budgets de performance (à mesurer et consigner dans `docs/performance.md`)

| Mesure | Cible |
|---|---|
| Démarrage à froid → fenêtre interactive | < 2 s (SSD), squelette affiché < 500 ms |
| RAM fenêtre ouverte (Accueil) | < 180 Mo |
| RAM en tray (fenêtre masquée) | < 90 Mo |
| CPU au repos (fenêtre visible) | < 1 % |
| CPU en tray | ≈ 0 % (aucun timer actif inutile) |
| Navigation entre onglets | < 100 ms perçus |
| Scan Steam (200 jeux installés) | < 2 s |
| Défilement de 2000 jeux | 60 fps (virtualisation) |

Moyens : ReadyToRun, chargement paresseux des modules (mixeur, stats, succès), `Freeze()` des brushes/images, décodage d'images à la taille d'affichage (`DecodePixelWidth`), caches bornés, suspension des activités quand masqué, aucune fuite d'événements (désabonnement / `WeakReferenceMessenger`).

---

## 20. Feuille de route par jalons

> L'agent exécute **tous** les jalons à la suite, sans demander de validation. À la fin de chaque jalon : `dotnet build` sans avertissement, `dotnet test` vert, commit (`feat:`/`fix:`/`test:`/`docs:`), mise à jour de `CHANGELOG.md`. Toute ambiguïté est tranchée selon ce document, puis consignée dans `DECISIONS.md`.

### Jalon 0 — Fondations
Solution, projets, `Directory.Build.props`, paquets centralisés, `.editorconfig`, licence MIT, README, CI, hosting/DI, Serilog, gestion globale des erreurs, instance unique, structure de dossiers `%AppData%`.
**Critères** : l'app démarre, affiche une fenêtre vide, log écrit, second lancement ramène la première fenêtre.

### Jalon 1 — Données, paramètres, sécurité
SQLite + migrations + sauvegardes + `quick_check` + restauration, `ISettingsService`, `ISecretStore` (DPAPI), `IConnectivityService`, masquage des secrets dans les logs.
**Critères** : tests de migration/corruption/DPAPI verts ; aucune donnée perdue lors d'un arrêt brutal simulé.

### Jalon 2 — Thème, fenêtre, navigation, localisation
Fenêtre sans bordure Mica/Snap, tokens, 3 thèmes, personnalisation (roue + hex), barre latérale, navigation animée, RESX FR/EN avec changement à chaud, parité testée.
**Critères** : changement de thème/langue sans redémarrage ; contraste vérifié ; DPI/multi-écran OK.

### Jalon 3 — Système
Tray, menu contextuel, réduire à la fermeture (réglable), démarrage auto par tâche planifiée avec repli `Run`, argument `--minimized`, paramètre fiable (état réel).
**Critères** : redémarrage de session → ARKADIA démarre minimisé sans UAC ; désactivation retire tâche et clé.

### Jalon 4 — Média et Sons
GSMTC + modal de source + widget d'en-tête, contrôle du volume de l'app source, ouverture de l'app native pour les playlists ; mixeur complet (périphériques, sessions, niveaux, profils), expérimental derrière drapeaux.
**Critères** : play/pause/suivant/précédent fonctionnent avec Spotify et Deezer ; mixeur reflète les changements Windows en temps réel ; aucune fuite COM ; aucun rafraîchissement onglet masqué.

### Jalon 5 — Moteur de jeux
Modèles, `IGameProvider`, providers Steam (local), Epic (local), Manuel, scan du PC + écran de validation, lancement/fermeture, `PlaytimeTracker`, page Jeux (virtualisée), fiche jeu, favoris/masqués.
**Critères** : détection correcte sur fixtures ; lancement 1 clic ; fermeture douce puis forcée proposée ; 2000 jeux fluides.

### Jalon 6 — Comptes et bibliothèques distantes
Steam OpenID (+ détection locale du compte) + assistant clé API + `GetOwnedGames`, CurseForge (local + API), Rinaorc (raccourci), Minecraft (mode dégradé + MSAL derrière drapeau), états de compte, installation via protocole.
**Critères** : parcours « Connecter Steam » en ≤ 3 clics avec compte détecté ; modes dégradés propres sans clés ; profil privé bien géré.

### Jalon 7 — Succès Steam
Synchronisation progressive, page Succès, cartes par jeu, détail, rareté, dates, tuile « dernier succès ».
**Critères** : quotas respectés (429), reprise après coupure réseau, affichage 100 % hors-ligne depuis le cache.

### Jalon 8 — Accueil et tuiles
`TileGridPanel`, mode édition, fusion/redimensionnement/glisser-déposer, persistance, toutes les tuiles par défaut, stats système + détails, raccourcis personnalisés (page Raccourcis).
**Critères** : algorithme de placement testé ; disposition survit au redémarrage ; tuile en erreur n'affecte pas les autres.

### Jalon 9 — Hors-ligne, performance, robustesse
Cache images LRU, stale-while-revalidate partout, bandeau hors-ligne, suspension en tray, mesures de performance, correction des fuites.
**Critères** : budgets de la section 19 atteints ou écarts documentés ; mode avion → app pleinement navigable.

### Jalon 10 — Télémétrie opt-in et données
Écran de consentement, file locale, visualiseur, envoi configurable, export/suppression des données.
**Critères** : rien ne sort sans consentement (test) ; retrait du consentement purge la file.

### Jalon 11 — Logo, installateur, mises à jour
Logo/icônes, Velopack, pack de release, mise à jour automatique, docs de signature, README final.
**Critères** : installation sans UAC ; mise à jour de v1.0.0 → v1.0.1 testée localement ; désinstallation propre.

### Jalon 12 — Recette finale
Passage complet de la check-list (section 22), nettoyage, documentation (`docs/`), `TODO_HUMAN.md` final, tag `v1.0.0`.

---

## 21. Règles de conduite pour l'agent

1. Lire ce document en entier ; ne rien inventer qui contredise la section 0.
2. Qualité avant vitesse : pas de TODO laissé dans le code livré (sauf lié à `TODO_HUMAN.md`).
3. **Dégradation gracieuse** : si une clé/API/permission manque, l'app fonctionne en mode limité avec un message clair. L'agent ne bloque jamais en attendant l'humain.
4. Toute action humaine requise est écrite dans `TODO_HUMAN.md` avec étapes précises : clé API Steam, clé API CurseForge, enregistrement Azure + demande d'approbation Mojang, confirmation Rinaorc, candidature SignPath, choix de l'endpoint de télémétrie.
5. Vérifier les **détails d'API marqués « à vérifier »** dans la documentation officielle avant implémentation et noter le résultat dans `DECISIONS.md`.
6. Aucun secret, clé ou identifiant personnel dans le dépôt. `.gitignore` strict.
7. Code : `async` partout, `CancellationToken`, injection de dépendances, pas de singletons statiques, pas de logique métier dans le code-behind, pas de chaînes en dur (RESX), commentaires seulement pour le « pourquoi ».
8. Commits petits et atomiques, messages conventionnels.
9. Ne jamais exécuter d'action destructrice sur les fichiers de l'utilisateur (jeux, launchers) ; tests uniquement sur des dossiers temporaires et des fixtures.
10. En fin de projet : fournir un rapport `docs/REPORT.md` (ce qui est fait, ce qui est dégradé, écarts de performance, limitations connues).

---

## 22. Check-list de recette finale (« pas de bug »)

**Démarrage & système**
- [ ] Installation sans UAC, désinstallation propre
- [ ] Démarrage avec Windows minimisé dans le tray ; désactivation efficace
- [ ] Second lancement ramène la fenêtre existante
- [ ] Fermer = réduire (et option quitter) ; menu tray complet

**Données**
- [ ] Arrêt brutal (kill du processus pendant une écriture) sans perte ni corruption
- [ ] Base corrompue volontairement → restauration automatique
- [ ] Secrets illisibles → compte « à reconnecter », pas de crash

**Réseau**
- [ ] Mode avion : navigation complète, données du cache, bandeau hors-ligne
- [ ] 429/5xx/timeouts gérés sans boucle ni blocage

**Jeux**
- [ ] Steam multi-bibliothèques, Epic, manuel, scan avec validation
- [ ] Jeu supprimé/déplacé → proposition « Localiser »
- [ ] Lancement/fermeture douce/forcée avec confirmation
- [ ] 2000 jeux : défilement fluide

**Succès**
- [ ] Profil public et privé gérés, jeux sans succès, succès cachés

**Média & Sons**
- [ ] Spotify, Deezer, YouTube Music : commandes et pochette
- [ ] App source fermée → bouton « Lancer » plutôt qu'erreur
- [ ] Mixeur : sessions apparaissent/disparaissent en direct, niveaux fluides, aucun usage CPU onglet masqué

**UI**
- [ ] Thèmes Bleu/Clair/Noir + personnalisation ; contraste signalé
- [ ] Changement de langue à chaud, aucune chaîne en dur
- [ ] DPI 100/125/150 %, multi-écrans, Snap Layouts
- [ ] Navigation clavier et focus visibles
- [ ] Tuiles : fusion, redimensionnement, glisser-déposer, persistance

**Confidentialité**
- [ ] Télémétrie désactivée par défaut, rien d'envoyé sans consentement
- [ ] Journaux sans secret ni chemin utilisateur sensible
- [ ] Export et suppression des données fonctionnels

---

## 23. Annexes

### 23.1 URI et chemins utiles (à vérifier à l'implémentation)
| Usage | Valeur |
|---|---|
| Lancer un jeu Steam | `steam://rungameid/{appid}` |
| Installer un jeu Steam | `steam://install/{appid}` |
| Page boutique Steam | `steam://store/{appid}` |
| Lancer un jeu Epic | `com.epicgames.launcher://apps/{AppName}?action=launch&silent=true` |
| Manifestes Epic | `C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item` |
| Dossier Steam | `HKCU\Software\Valve\Steam\SteamPath` |
| Compte Steam actif | `HKCU\Software\Valve\Steam\ActiveProcess\ActiveUser` ; `config\loginusers.vdf` |
| Spotify | `spotify:` |
| Deezer | `deezer://www.deezer.com/` |
| YouTube Music | `https://music.youtube.com/` |
| Clé API Steam | `https://steamcommunity.com/dev/apikey` |
| Console CurseForge | `https://console.curseforge.com/` |

### 23.2 Cas limites à couvrir
- Steam non installé ; Steam installé sur plusieurs disques dont un déconnecté
- Plusieurs comptes Steam sur le même PC ; partage familial
- Noms de jeux avec Unicode/emoji ; chemins > 260 caractères ; noms d'utilisateur Windows avec accents
- Jeux dont le dossier d'installation a été renommé
- Profil Windows restauré (DPAPI invalide)
- Mise en veille/réveil pendant une synchronisation
- Changement de périphérique audio pendant la lecture
- Effets de transparence Windows désactivés ; mode contraste élevé
- Horloge système incorrecte (jetons, dates de succès)

### 23.3 Glossaire
GSMTC : System Media Transport Controls (session média Windows). DPAPI : API de protection des données Windows. WAL : journal en écriture anticipée SQLite. OpenID : protocole d'identité (utilisé par Steam). MSAL : bibliothèque d'authentification Microsoft.

---

*Fin du cahier des charges v2.*
