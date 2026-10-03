# Regenerates the unified settings (Visual Studio 2026, Tools > Options > Kubuno) from the single table below:
#   src/Kubuno.VisualStudio/UnifiedSettings/kubuno.registration.json   (the registration manifest)
#   src/Kubuno.VisualStudio/Resources/Settings.resx / Settings.fr.resx (English default + French labels)
# The manifest points at the resources with "@Name;..\Kubuno.VisualStudio.dll" (the form Roslyn's own manifests use).
# Usage: powershell -File tools/gen-unified-settings.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$proj = Join-Path $root 'src\Kubuno.VisualStudio'
$asm = '..\Kubuno.VisualStudio.dll'

# Categories: moniker, English title, French title, English description, French description
$categories = @(
    @('kubuno', 'Kubuno', 'Kubuno', 'Rust and Kubuno views development in Visual Studio.', 'Developpement Rust et vues Kubuno dans Visual Studio.'),
    @('kubuno.rust', 'Rust', 'Rust', 'rust-analyzer, editor features and inline hints for .rs files.', 'rust-analyzer, fonctions de l''editeur et indications inline pour les fichiers .rs.'),
    @('kubuno.rust.rustAnalyzer', 'rust-analyzer', 'rust-analyzer', 'The Rust language server.', 'Le serveur de langage Rust.'),
    @('kubuno.rust.editor', 'Editor', 'Editeur', 'Editing features of .rs files.', 'Fonctions d''edition des fichiers .rs.'),
    @('kubuno.rust.diagnostics', 'Diagnostics', 'Diagnostics', 'Logging of the Rust language server.', 'Journalisation du serveur de langage Rust.'),
    @('kubuno.rust.inlayHints', 'Inline hints', 'Indications inline', 'The grey hints rust-analyzer adds inside the code (types, parameter names...), like C#''s inline hints.', 'Les indications grises que rust-analyzer ajoute dans le code (types, noms de parametres...), comme les indications inline de C#.'),
    @('kubuno.rust.inlayHints.parameterNames', 'Parameter names', 'Noms de parametres', 'Inline parameter name hints.', 'Indications inline de noms de parametres.'),
    @('kubuno.rust.inlayHints.types', 'Types', 'Types', 'Inline type hints.', 'Indications inline de types.'),
    @('kubuno.rust.inlayHints.other', 'Other hints', 'Autres indications', 'Other inline hints.', 'Autres indications inline.'),
    @('kubuno.views', 'Views', 'Vues', 'The .kbview language server.', 'Le serveur de langage des fichiers .kbview.'),
    @('kubuno.views.languageServer', 'kubuno-views-ls', 'kubuno-views-ls', 'The .kbview language server.', 'Le serveur de langage des fichiers .kbview.'),
    @('kubuno.designer', 'Designer', 'Concepteur', 'The Kubuno View Designer.', 'Le concepteur de vues Kubuno.'),
    @('kubuno.designer.editor', 'Editor', 'Editeur', 'How .kbview files are opened.', 'Comment les fichiers .kbview sont ouverts.'),
    @('kubuno.designer.surface', 'Design surface', 'Surface de conception', 'What the design surface draws.', 'Ce que la surface de conception dessine.'),
    @('kubuno.debugging', 'Debugging', 'Debogage', 'Debugging of Rust and Kubuno applications.', 'Debogage des applications Rust et Kubuno.'),
    @('kubuno.debugging.justMyCode', 'Just My Code', 'Uniquement mon code', 'Which frames the debugger shows.', 'Les cadres que le debogueur affiche.'),
    @('kubuno.data', 'Data', 'Donnees', 'The Data Explorer and its query windows.', 'L''Explorateur de donnees et ses fenetres de requete.'),
    @('kubuno.data.explorer', 'Data Explorer', 'Explorateur de donnees', 'Connections, schema tree and table data.', 'Connexions, arborescence du schema et donnees des tables.'),
    @('kubuno.data.query', 'Query window', 'Fenetre de requete', 'Running SQL queries.', 'Execution des requetes SQL.'),
    @('kubuno.data.diagnostics', 'Diagnostics', 'Diagnostics', 'Logging of the data helper (kubuno-data-tool).', 'Journalisation de l''assistant de donnees (kubuno-data-tool).'),
    # French texts of the remote host entries are written with their accents (this file is UTF-8 with a BOM).
    @('kubuno.remote', 'Remote Linux host', 'Hôte Linux distant', 'The Linux machine that holds the development database (through an SSH tunnel) and, later, the Linux builds.', 'La machine Linux qui héberge la base de développement (par un tunnel SSH) et, plus tard, les builds Linux.'),
    @('kubuno.remote.connection', 'Connection', 'Connexion', 'How Kubuno connects to the host: non-interactive SSH with a dedicated key, never a password.', 'Comment Kubuno se connecte à l''hôte : SSH non interactif avec une clé dédiée, jamais de mot de passe.'),
    @('kubuno.remote.devDatabase', 'Development database', 'Base de développement', 'The SSH tunnel the Kubuno Core Web launch profiles open to the host''s PostgreSQL.', 'Le tunnel SSH que les profils de lancement de Kubuno Core Web ouvrent vers le PostgreSQL de l''hôte.')
)

# Properties: moniker, type (boolean/string/enum), default, English title, French title, English description, French description,
#             enum values (value=EnglishLabel|FrenchLabel;...), extras (hashtable)
$properties = @(
    @{ m = 'kubuno.rust.rustAnalyzer.pathOverride'; t = 'string'; d = ''; extra = @{ format = 'path'; pathKind = 'file' }
       en = 'Path override'; fr = 'Chemin personnalise'
       den = 'Full path to rust-analyzer.exe. When empty, Kubuno tries "rustup which rust-analyzer", then %USERPROFILE%\.cargo\bin\rust-analyzer.exe, then the PATH environment variable. Applies the next time the server starts.'
       dfr = 'Chemin complet de rust-analyzer.exe. Vide, Kubuno essaie "rustup which rust-analyzer", puis %USERPROFILE%\.cargo\bin\rust-analyzer.exe, puis la variable d''environnement PATH. Pris en compte au prochain demarrage du serveur.' },
    @{ m = 'kubuno.rust.editor.formatOnSave'; t = 'boolean'; d = $false
       en = 'Format on save'; fr = 'Mettre en forme a l''enregistrement'
       den = 'Run Format Document (rustfmt, through rust-analyzer) on every .rs file before it is saved.'
       dfr = 'Execute Mettre en forme le document (rustfmt, via rust-analyzer) sur chaque fichier .rs avant son enregistrement.' },
    @{ m = 'kubuno.rust.editor.codeLens'; t = 'boolean'; d = $true
       en = 'Show CodeLens'; fr = 'Afficher CodeLens'
       den = 'Show reference and implementation counts ("3 references") above Rust items, like C#''s CodeLens. Click a count to open Find All References or the implementations.'
       dfr = 'Affiche le nombre de references et d''implementations ("3 references") au-dessus des elements Rust, comme CodeLens en C#. Cliquez sur un nombre pour ouvrir Rechercher toutes les references ou les implementations.' },
    @{ m = 'kubuno.rust.diagnostics.lspTrace'; t = 'enum'; d = 'off'; e = 'off=Off|Desactive;messages=Messages|Messages;verbose=Verbose|Detaille'
       en = 'LSP trace'; fr = 'Trace LSP'
       den = 'How much of the traffic between Visual Studio and rust-analyzer is logged in the Kubuno Output pane. Off logs only the startup line and errors; Messages adds one line per request and response; Verbose adds the raw JSON.'
       dfr = 'Quantite de trafic entre Visual Studio et rust-analyzer journalisee dans le volet Sortie Kubuno. Desactive n''enregistre que le demarrage et les erreurs; Messages ajoute une ligne par requete et reponse; Detaille ajoute le JSON brut.' },
    @{ m = 'kubuno.rust.inlayHints.show'; t = 'enum'; d = 'whilePressingAltF1'; e = 'whilePressingAltF1=While pressing Alt+F1|Pendant l''appui sur Alt+F1;always=Always|Toujours;visualStudioSetting=Visual Studio setting|Parametre de Visual Studio;never=Never|Jamais'
       en = 'Show inline hints'; fr = 'Afficher les indications inline'
       den = 'When the hints are shown in .rs files. "While pressing Alt+F1" is C#''s default (hold Alt+F1 to see them); "Visual Studio setting" follows Text Editor > All Languages > Inlay Hints. Which hints exist is chosen below.'
       dfr = 'Quand les indications s''affichent dans les fichiers .rs. "Pendant l''appui sur Alt+F1" est le comportement par defaut de C# (maintenez Alt+F1 pour les voir); "Parametre de Visual Studio" suit Editeur de texte > Tous les langages > Indications inlay. Les indications disponibles se choisissent ci-dessous.' },
    @{ m = 'kubuno.rust.inlayHints.parameterNames.enabled'; t = 'boolean'; d = $true
       en = 'Display inline parameter name hints'; fr = 'Afficher les indications inline de noms de parametres'
       den = 'Show the parameter''s name before each argument ("title: name"). rust-analyzer already leaves the hint out when the argument is named like the parameter.'
       dfr = 'Affiche le nom du parametre avant chaque argument ("title: name"). rust-analyzer omet deja l''indication quand l''argument porte le nom du parametre.' },
    @{ m = 'kubuno.rust.inlayHints.parameterNames.forLiterals'; t = 'boolean'; d = $true
       en = 'Show hints for literals'; fr = 'Afficher pour les litteraux'
       den = 'Keep the parameter name hint when the argument is a literal (5, "text", true, ''c'').'
       dfr = 'Conserve l''indication du nom du parametre quand l''argument est un litteral (5, "text", true, ''c'').'; when = '${config:kubuno.rust.inlayHints.parameterNames.enabled} == ''true''' },
    @{ m = 'kubuno.rust.inlayHints.parameterNames.forOtherArguments'; t = 'boolean'; d = $true
       en = 'Show hints for everything else'; fr = 'Afficher pour tout le reste'
       den = 'Keep the parameter name hint when the argument is a variable, a call or any other expression that is not a literal.'
       dfr = 'Conserve l''indication du nom du parametre quand l''argument est une variable, un appel ou toute autre expression qui n''est pas un litteral.'; when = '${config:kubuno.rust.inlayHints.parameterNames.enabled} == ''true''' },
    @{ m = 'kubuno.rust.inlayHints.types.enabled'; t = 'boolean'; d = $true
       en = 'Display inline type hints'; fr = 'Afficher les indications inline de types'
       den = 'Show the inferred type after a variable (": Vec<u8>").'
       dfr = 'Affiche le type deduit apres une variable (": Vec<u8>").' },
    @{ m = 'kubuno.rust.inlayHints.types.hideApparent'; t = 'boolean'; d = $true
       en = 'Suppress hints when the type is apparent'; fr = 'Masquer quand le type est evident'
       den = 'No type hint when the initializer names the type (let w = Widget::new()).'
       dfr = 'Pas d''indication de type quand l''initialiseur nomme le type (let w = Widget::new()).'; when = '${config:kubuno.rust.inlayHints.types.enabled} == ''true''' },
    @{ m = 'kubuno.rust.inlayHints.types.hideClosureVariables'; t = 'boolean'; d = $true
       en = 'Suppress hints on variables holding a closure'; fr = 'Masquer sur les variables contenant une fermeture'
       den = 'No type hint on a variable initialized with a closure (let f = |x| x + 1): the closure''s own type is unreadable noise.'
       dfr = 'Pas d''indication de type sur une variable initialisee avec une fermeture (let f = |x| x + 1): le type propre de la fermeture est illisible.'; when = '${config:kubuno.rust.inlayHints.types.enabled} == ''true''' },
    @{ m = 'kubuno.rust.inlayHints.types.closureParameters'; t = 'boolean'; d = $true
       en = 'Show hints for closure parameter types'; fr = 'Afficher pour les types des parametres de fermetures'
       den = 'Show the type of a closure''s parameters, like C#''s lambda parameter types.'
       dfr = 'Affiche le type des parametres d''une fermeture, comme les types de parametres de lambda en C#.' },
    @{ m = 'kubuno.rust.inlayHints.other.methodChains'; t = 'boolean'; d = $true
       en = 'Show types at the end of method chains'; fr = 'Afficher les types en fin de chaine de methodes'
       den = 'Show the type at the end of each line of a method chain.'
       dfr = 'Affiche le type a la fin de chaque ligne d''une chaine d''appels de methodes.' },
    @{ m = 'kubuno.rust.inlayHints.other.closureReturnTypes'; t = 'enum'; d = 'never'; e = 'never=Never|Jamais;withBlock=Only for closures with a block|Seulement pour les fermetures a bloc;always=Always|Toujours'
       en = 'Closure return types'; fr = 'Types de retour des fermetures'
       den = 'Show a closure''s return type: never, only when its body is a block, or always.'
       dfr = 'Affiche le type de retour d''une fermeture: jamais, seulement quand son corps est un bloc, ou toujours.' },
    @{ m = 'kubuno.rust.inlayHints.other.lifetimeElision'; t = 'enum'; d = 'never'; e = 'never=Never|Jamais;skipTrivial=Skip trivial cases|Ignorer les cas triviaux;always=Always|Toujours'
       en = 'Elided lifetimes'; fr = 'Durees de vie elidees'
       den = 'Show the lifetimes Rust infers in signatures (fn f<''a>(x: &''a T) -> &''a U): never, skipping the trivial ones, or always.'
       dfr = 'Affiche les durees de vie deduites par Rust dans les signatures (fn f<''a>(x: &''a T) -> &''a U): jamais, en ignorant les triviales, ou toujours.' },
    @{ m = 'kubuno.rust.inlayHints.other.bindingModes'; t = 'boolean'; d = $false
       en = 'Binding modes'; fr = 'Modes de liaison'
       den = 'Show the implicit & and ref of patterns matched through a reference.'
       dfr = 'Affiche les & et ref implicites des motifs appliques a travers une reference.' },
    @{ m = 'kubuno.rust.inlayHints.other.closingBraces'; t = 'boolean'; d = $false
       en = 'Names after closing braces'; fr = 'Noms apres les accolades fermantes'
       den = 'Show the name of the item after the closing brace of a long block ("// fn main").'
       dfr = 'Affiche le nom de l''element apres l''accolade fermante d''un long bloc ("// fn main").' },
    @{ m = 'kubuno.views.languageServer.pathOverride'; t = 'string'; d = ''; extra = @{ format = 'path'; pathKind = 'file' }
       en = 'Path override'; fr = 'Chemin personnalise'
       den = 'Full path to kubuno-views-ls.exe. When empty, Kubuno tries the extension''s own tools folder, then the PATH environment variable, then the local development build folders.'
       dfr = 'Chemin complet de kubuno-views-ls.exe. Vide, Kubuno essaie le dossier tools de l''extension, puis la variable d''environnement PATH, puis les dossiers de build de developpement locaux.' },
    @{ m = 'kubuno.designer.editor.useAsDefault'; t = 'boolean'; d = $false
       en = 'Use the View Designer as the default editor'; fr = 'Utiliser le concepteur de vues comme editeur par defaut'
       den = 'A double-click on a .kbview file opens the Kubuno View Designer (Design | XML split view) instead of the plain XML editor. Either way the designer is reachable through Open With.'
       dfr = 'Un double-clic sur un fichier .kbview ouvre le concepteur de vues Kubuno (vue fractionnee Design | XML) au lieu de l''editeur XML simple. Dans les deux cas, le concepteur reste accessible par Ouvrir avec.' },
    @{ m = 'kubuno.designer.surface.showDesignOutlines'; t = 'boolean'; d = $true
       en = 'Show design outlines'; fr = 'Afficher les contours de conception'
       den = 'Draws a faint dashed outline around the containers that are otherwise invisible (a Panel or Stack with no surface, border or background), so they can be found and dropped into, like the dotted border Windows Forms shows around a borderless Panel. Controls always look exactly as at run time. Applies to the designers opened afterwards.'
       dfr = 'Dessine un fin contour en pointilles autour des conteneurs autrement invisibles (un Panel ou un Stack sans surface, bordure ni fond), pour les reperer et y deposer des controles, comme la bordure pointillee de Windows Forms autour d''un Panel sans bordure. Les controles gardent toujours leur apparence d''execution. Pris en compte par les concepteurs ouverts ensuite.' },
    @{ m = 'kubuno.debugging.justMyCode.frameworkIsExternalCode'; t = 'boolean'; d = $true
       en = 'Treat the Kubuno framework as external code'; fr = 'Traiter le framework Kubuno comme du code externe'
       den = 'Like Windows Forms for a C# application: with Just My Code on, the Call Stack collapses the Kubuno framework (kubuno_views, kubuno_controls, kubuno_ui, the event-handler dispatch glue) into [External Code] and Step Into (F11) goes straight to your handlers. Turn it off to see and debug Kubuno''s own code. The Rust standard library is always stepped over. Takes effect at the next debug session.'
       dfr = 'Comme Windows Forms pour une application C#: avec Uniquement mon code active, la Pile des appels replie le framework Kubuno (kubuno_views, kubuno_controls, kubuno_ui, la colle d''envoi des gestionnaires d''evenements) en [Code externe] et Pas a pas detaille (F11) va directement a vos gestionnaires. Decochez pour voir et deboguer le code de Kubuno lui-meme. La bibliotheque standard de Rust est toujours ignoree. Pris en compte a la prochaine session de debogage.' },
    @{ m = 'kubuno.data.explorer.showDataRows'; t = 'integer'; d = 200; extra = @{ minimum = 1; maximum = 100000 }
       en = 'Rows shown by Show Table Data'; fr = 'Lignes lues par Afficher les donnees'
       den = 'How many rows "Show Table Data" reads from a table or view (1 to 100000).'
       dfr = 'Nombre de lignes que "Afficher les donnees" lit dans une table ou une vue (1 a 100000).' },
    @{ m = 'kubuno.data.explorer.defaultCredentialStore'; t = 'enum'; d = 'credentialManager'; e = 'credentialManager=Windows Credential Manager|Gestionnaire d''identification Windows;userSecrets=User secrets|Secrets utilisateur'
       en = 'Default credential storage'; fr = 'Stockage initial des informations d''identification'
       den = 'Where the Add Connection dialog proposes to store a new connection string. Either way the string is never written into a project.'
       dfr = 'Emplacement propose par la boite de dialogue Ajouter une connexion pour une nouvelle chaine de connexion. Dans les deux cas, la chaine n''est jamais ecrite dans un projet.' },
    @{ m = 'kubuno.data.query.maxRows'; t = 'integer'; d = 1000; extra = @{ minimum = 1; maximum = 1000000 }
       en = 'Maximum rows per result'; fr = 'Nombre maximal de lignes par resultat'
       den = 'A result set is cut after this many rows; the status bar of the query window says so (1 to 1000000).'
       dfr = 'Un jeu de resultats est tronque apres ce nombre de lignes; la barre d''etat de la fenetre de requete l''indique (1 a 1000000).' },
    @{ m = 'kubuno.data.query.timeoutSeconds'; t = 'integer'; d = 30; extra = @{ minimum = 1; maximum = 86400 }
       en = 'Query timeout (seconds)'; fr = 'Delai d''execution des requetes (secondes)'
       den = 'How long a query may run before it is cancelled (1 to 86400).'
       dfr = 'Duree maximale d''une requete avant son annulation (1 a 86400).' },
    @{ m = 'kubuno.data.diagnostics.logRequests'; t = 'boolean'; d = $false
       en = 'Log data helper requests'; fr = 'Journaliser les requetes de l''assistant de donnees'
       den = 'Log each request to kubuno-data-tool (method and parameters, with connection strings, passwords and SQL text removed) in the Kubuno Output pane.'
       dfr = 'Journalise chaque requete envoyee a kubuno-data-tool (methode et parametres, sans chaines de connexion, mots de passe ni texte SQL) dans le volet Sortie Kubuno.' },
    @{ m = 'kubuno.remote.connection.host'; t = 'string'; d = '192.168.1.220'
       en = 'Host'; fr = 'Hôte'
       den = 'Name or IP address of the remote Linux host.'
       dfr = 'Nom ou adresse IP de l''hôte Linux distant.' },
    @{ m = 'kubuno.remote.connection.user'; t = 'string'; d = 'martinien'
       en = 'User'; fr = 'Utilisateur'
       den = 'The account Kubuno connects as. Its ~/.ssh/authorized_keys must hold the public key (the .pub file next to the private key).'
       dfr = 'Le compte sous lequel Kubuno se connecte. Son fichier ~/.ssh/authorized_keys doit contenir la clé publique (le fichier .pub à côté de la clé privée).' },
    @{ m = 'kubuno.remote.connection.port'; t = 'integer'; d = 22; extra = @{ minimum = 1; maximum = 65535 }
       en = 'SSH port'; fr = 'Port SSH'
       den = 'The port of the host''s SSH server (1 to 65535).'
       dfr = 'Le port du serveur SSH de l''hôte (1 à 65535).' },
    @{ m = 'kubuno.remote.connection.privateKeyPath'; t = 'string'; d = '%USERPROFILE%\.ssh\id_ed25519_kubuno'; extra = @{ format = 'path'; pathKind = 'file' }
       en = 'Private key'; fr = 'Clé privée'
       den = 'The private key Kubuno authenticates with, used by Windows'' OpenSSH in batch mode: it must have no passphrase, since Kubuno never asks for a password or a passphrase. Environment variables (%USERPROFILE%) are expanded.'
       dfr = 'La clé privée avec laquelle Kubuno s''authentifie, utilisée par l''OpenSSH de Windows en mode batch : elle ne doit pas avoir de phrase secrète, Kubuno ne demandant jamais de mot de passe ni de phrase secrète. Les variables d''environnement (%USERPROFILE%) sont développées.' },
    @{ m = 'kubuno.remote.connection.knownHostsPath'; t = 'string'; d = '%USERPROFILE%\.ssh\known_hosts_kubuno'; extra = @{ format = 'path'; pathKind = 'file' }
       en = 'Known hosts file'; fr = 'Fichier des hôtes connus'
       den = 'The host keys Kubuno accepts, kept apart from your own known_hosts. An unknown host is never accepted silently: an info bar shows its fingerprint and adds it here only when you click Accept.'
       dfr = 'Les clés d''hôte que Kubuno accepte, séparées de votre propre known_hosts. Un hôte inconnu n''est jamais accepté en silence : une barre d''informations affiche son empreinte et ne l''ajoute ici que si vous cliquez sur Accepter.' },
    @{ m = 'kubuno.remote.devDatabase.tunnel'; t = 'enum'; d = 'auto'; e = 'auto=When KUBUNO_DEV_DATABASE_URL points at the local port|Quand KUBUNO_DEV_DATABASE_URL vise le port local;always=At every launch|À chaque lancement;never=Never|Jamais'
       en = 'SSH tunnel'; fr = 'Tunnel SSH'
       den = 'When F5 / Ctrl+F5 of the Kubuno Core Web profiles opens the tunnel from localhost to the host''s PostgreSQL (ssh -N -L). It is reused by the next launches and closed with Visual Studio; errors appear in an info bar and the Kubuno Output pane.'
       dfr = 'Quand F5 / Ctrl+F5 des profils Kubuno Core Web ouvre le tunnel de localhost vers le PostgreSQL de l''hôte (ssh -N -L). Il est réutilisé par les lancements suivants et fermé avec Visual Studio ; les erreurs s''affichent dans une barre d''informations et le volet Sortie Kubuno.' },
    @{ m = 'kubuno.remote.devDatabase.localPort'; t = 'integer'; d = 55432; extra = @{ minimum = 1; maximum = 65535 }
       en = 'Local port'; fr = 'Port local'
       den = 'The tunnel''s local end: KUBUNO_DEV_DATABASE_URL then reads postgres://kubuno:<password>@localhost:55432/kubuno_dev.'
       dfr = 'L''extrémité locale du tunnel : KUBUNO_DEV_DATABASE_URL vaut alors postgres://kubuno:<mot de passe>@localhost:55432/kubuno_dev.' },
    @{ m = 'kubuno.remote.devDatabase.remotePort'; t = 'integer'; d = 5432; extra = @{ minimum = 1; maximum = 65535 }
       en = 'PostgreSQL port on the host'; fr = 'Port PostgreSQL sur l''hôte'
       den = 'The port PostgreSQL listens on, on the host''s own loopback (it does not need to be reachable from the network).'
       dfr = 'Le port d''écoute de PostgreSQL, sur la boucle locale de l''hôte (il n''a pas besoin d''être joignable depuis le réseau).' }
)

# French strings are written without accents in the table above (ASCII source); this restores them (case-sensitive pairs).
$accentPairs = @'
 a l' =>  à l'
 a la  =>  à la 
 a vos  =>  à vos 
 a travers  =>  à travers 
 a bloc =>  à bloc
 a chaque =>  à chaque
 a pas  =>  à pas 
Desactive => Désactivé
reponse => réponse
lui-meme => lui-même
Decochez => Décochez
code active, => code activé,
Detaille => Détaillé
detaille => détaillé
Parametre => Paramètre
parametre => paramètre
Developpement => Développement
developpement => développement
Debogage => Débogage
debogage => débogage
deboguer => déboguer
debogueur => débogueur
Editeur => Éditeur
editeur => éditeur
edition => édition
Execute => Exécute
Durees => Durées
durees => durées
elidees => élidées
deduit => déduit
evident => évident
litteral => littéral
references => références
implementations => implémentations
journalisee => journalisée
Quantite => Quantité
demarrage => démarrage
requete => requête
personnalise => personnalisé
fractionnee => fractionnée
deja => déjà
ignoree => ignorée
bibliotheque => bibliothèque
appliques => appliqués
elements => éléments
element => élément
evenements => événements
duree => durée
Duree => Durée
Donnees => Données
donnees => données
Fenetre => Fenêtre
fenetre => fenêtre
schema => schéma
Execution => Exécution
execution => exécution
Delai => Délai
resultat => résultat
tronque apres => tronqué après
d'etat => d'état
propose par => proposé par
boite => boîte
ecrite => écrite
chaine de connexion => chaîne de connexion
la chaine n => la chaîne n
chaines de => chaînes de
envoyee a => envoyée à
methode et => méthode et
(1 a 1 => (1 à 1
(1 a 8 => (1 à 8
pointilles => pointillés
pointillee => pointillée
les reperer => les repérer
deposer des controles => déposer des contrôles
Les controles => Les contrôles
'@
$accents = @(); foreach ($line in ($accentPairs -split "`r?`n")) { if ($line -match ' => ') { $p = $line -split ' => ', 2; $accents += ,@($p[0], $p[1]) } }
function Fr([string]$s) { foreach ($a in $accents) { $s = $s -creplace [regex]::Escape($a[0]), $a[1] }; $s }
function KeyOf([string]$moniker, [string]$part) { 'S_' + ($moniker -replace '\.', '_') + '_' + $part }
function Res([string]$key) { "@$key;$asm" }
$en = [ordered]@{}; $fr = [ordered]@{}
$json = [ordered]@{}

$cats = [ordered]@{}
$order = 0
foreach ($c in $categories) {
    $m = $c[0]
    $en[(KeyOf $m 'Title')] = $c[1]; $fr[(KeyOf $m 'Title')] = Fr $c[2]
    $en[(KeyOf $m 'Description')] = $c[3]; $fr[(KeyOf $m 'Description')] = Fr $c[4]
    $cat = [ordered]@{ title = (Res (KeyOf $m 'Title')); description = (Res (KeyOf $m 'Description')); order = $order }
    $cats[$m] = $cat
    $order += 10
}

$props = [ordered]@{}
$order = 0
foreach ($p in $properties) {
    $m = $p.m
    $en[(KeyOf $m 'Title')] = $p.en; $fr[(KeyOf $m 'Title')] = Fr $p.fr
    $en[(KeyOf $m 'Description')] = $p.den; $fr[(KeyOf $m 'Description')] = Fr $p.dfr
    $o = [ordered]@{}
    if ($p.t -eq 'enum') { $o['type'] = 'string' } else { $o['type'] = $p.t }
    $o['title'] = Res (KeyOf $m 'Title')
    $o['description'] = Res (KeyOf $m 'Description')
    $o['default'] = $p.d
    $o['order'] = $order
    if ($p.t -eq 'enum') {
        $values = @(); $labels = @()
        foreach ($item in $p.e.Split(';')) {
            $v, $names = $item.Split('=', 2)
            $n = $names.Split('|')
            $values += $v
            $k = KeyOf $m ("Item_" + $v)
            $en[$k] = $n[0]; $fr[$k] = Fr $n[1]
            $labels += (Res $k)
        }
        $o['enum'] = $values
        $o['enumItemLabels'] = $labels
    }
    if ($p.extra) { foreach ($k in $p.extra.Keys) { $o[$k] = $p.extra[$k] } }
    if ($p.when) { $o['enableWhen'] = $p.when }
    $props[$m] = $o
    $order += 10
}

$manifest = [ordered]@{ '$schema' = 'registration.schema.json'; properties = $props; categories = $cats }
$dir = Join-Path $proj 'UnifiedSettings'
New-Item -ItemType Directory -Force $dir | Out-Null
$text = ($manifest | ConvertTo-Json -Depth 10)
# Unicode escapes are turned back into characters; ConvertTo-Json escapes < > ' & as \u00XX.
$text = [regex]::Replace($text, '\\u([0-9a-fA-F]{4})', { param($mt) [string][char][Convert]::ToInt32($mt.Groups[1].Value, 16) })
[IO.File]::WriteAllText((Join-Path $dir 'kubuno.registration.json'), $text + "`n", (New-Object Text.UTF8Encoding($false)))

function WriteResx([string]$path, $table) {
    $sb = New-Object Text.StringBuilder
    [void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
    [void]$sb.AppendLine('<root>')
    [void]$sb.AppendLine('  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>')
    [void]$sb.AppendLine('  <resheader name="version"><value>2.0</value></resheader>')
    [void]$sb.AppendLine('  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>')
    [void]$sb.AppendLine('  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>')
    foreach ($k in $table.Keys) {
        $v = [Security.SecurityElement]::Escape([string]$table[$k])
        [void]$sb.AppendLine("  <data name=`"$k`" xml:space=`"preserve`"><value>$v</value></data>")
    }
    [void]$sb.AppendLine('</root>')
    [IO.File]::WriteAllText($path, $sb.ToString(), (New-Object Text.UTF8Encoding($false)))
}
$res = Join-Path $proj 'Resources'
WriteResx (Join-Path $res 'Settings.resx') $en
WriteResx (Join-Path $res 'Settings.fr.resx') $fr
"Generated $($props.Count) settings, $($cats.Count) categories, $($en.Count) strings."
