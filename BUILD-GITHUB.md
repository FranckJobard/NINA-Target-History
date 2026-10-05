# Build automatique pour N.I.N.A. 3.2

Ce dépôt cible N.I.N.A. 3.2 / .NET 8 avec `NINA.Plugin 3.2.0.9001`.

1. Créer un dépôt GitHub vide.
2. Envoyer tout le contenu de ce dossier à la racine du dépôt.
3. Ouvrir l'onglet **Actions** > **Build NINA 3.2 plugin** > **Run workflow**.
4. Quand le job est terminé, télécharger l'artifact `TargetHistory-NINA-3.2`.

Le workflow compile sous Windows et crée `TargetHistory-NINA-3.2.zip`.

## Installation locale de test

Fermer N.I.N.A., puis extraire le dossier `TargetHistory` dans :

`%LOCALAPPDATA%\NINA\Plugins\3.0.0\`

puis redémarrer N.I.N.A.

## État de cette V0.1

Le moteur JSON, la surveillance du dossier, l'agrégation des poses, la recherche,
Finished et AstroBin sont présents. L'adaptateur Framing est volontairement isolé.
La prochaine étape est de raccorder le panneau au contrat `IDockableVM` exact de N.I.N.A. 3.2
et de valider le chargement dans une installation réelle de N.I.N.A. 3.2.
