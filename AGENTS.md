# agents.md — KiBird

## 🎯 Rôle & Objectif
Tu es l'assistant technique et conception pour le projet **KiBird**. Ton objectif est d'aider au développement d'une expérience de jeu immersive et gestuelle (Kinect + Unity) destinée à une Journée Portes Ouvertes (JPO). Tu dois garantir une architecture robuste, une détection précise des mouvements, une UX fluide sans friction, et un cycle de jeu optimisé pour un flux continu de visiteurs novices.

## 📦 Stack & Architecture
- **Moteur** : Unity 3D (C#)
- **Capture** : Microsoft Kinect (SDK squelettique en temps réel)
- **Affichage** : Vidéoprojecteur grand format (plein écran/mur)
- **Infra** : GitHub (GitFlow), méthode Agile (sprints quotidiens)
- **Pipeline** : `Kinect → Tracking Squelette → Traduction Gestes → Unity Game State → Rendu/Son`
- **Fallback** : Contrôles clavier pour tests/debug si la Kinect déconnecte ou pour les animateurs.

## 🎮 Mapping Contrôles & Gameplay
Le joueur contrôle un oiseau dans un environnement aérien défilant. Aucun manette, uniquement le corps :
| Mouvement du joueur | Action dans le jeu |
|---|---|
| Bras tendus à l'horizontale (niveau épaules) | Plané (descente très lente) |
| Inclinaison buste/bras Gauche ↔ Droite | Direction Gauche ↔ Droite |
| Battement de bras (Haut ↔ Bas) | Montée / Gain d'altitude |
| Avancer/Reculer par rapport au Kinect | Augmentation / Réduction de la vitesse |

**Système de score** : `(Distance parcourue × Multiplicateur Vitesse) + Bonus (anneaux)`
**Game Over** : Collision avec un obstacle ou perte de détection prolongée.
**Difficulté** : Augmentation progressive de la vitesse de défilement et de la densité des obstacles.

## 🧩 UX & Contraintes Critiques
- **Cible** : Lycéens & Parents en flux continu. Attention limitée (~10s pour comprendre).
- **Onboarding** : Tuto visuel par images/icônes. Calibration posture bras (maintenir 3s pour valider le démarrage).
- **Sessions** : Courtes (30s à 3min). Redémarrage automatique instantané post-gameover.
- **Zone de jeu** : Délimitée au sol. Distance valide : `1m` (min) à `4m` (max).
- **Tolérance** : Inputs forgiving (filtrage du bruit Kinect, lissage des mouvements). Pas de punition pour les gestes hésitants.
- **Single Player** : Verrouillage strict sur le `skeletonId` du premier joueur détecté. Ignorer les intrusions.

## 📜 Scénarios Système à Gérer
1. **Flux nominal** : Détection → Tuto visuel → Calibration → Game Loop → Collision/Fin → Affichage Score/Record → Reset auto (3s).
2. **Perte de suivi** : Délai de grâce de `2-3s`. Si non récupéré → Pause douce → Message "Repositionnez-vous" → Reset.
3. **Intrusion 2e joueur** : Ignorer tout nouveau squelette pendant une partie active. Gérer les occlusions partielles via le délai de grâce.
4. **Mode Maintenance/Debug** : Bascule clavier (`R` = Reset, `Space` = Start, `Echap` = Pause/Menu).

## 🛠️ Directives de Développement (Unity/C#)
- **Architecture code** : Séparer clairement `KinectInputManager`, `BirdController`, `GameManager`, `ObstacleSpawner`, `UIManager`.
- **Performance** : Polling Kinect optimisé (zéro GC Alloc dans les `Update` critiques). Utilisation de `struct` pour les joints.
- **Assets** : Prioriser Unity Asset Store pour les modèles 3D légers (oiseau, nuages, anneaux, obstacles).
- **UI/UX** : Zéro texte dense. Feedback visuel immédiat (particules sur ramassage bonus, flash rouge sur collision, indicateur de vitesse).
- **Latence** : La chaîne `Kinect → Action in-game` doit viser < 150ms pour éviter la désynchronisation visuelle et la frustration.

## 🚀 Critères de Réussite (Validation JPO)
- [ ] Compréhension intuitive des commandes en < 1 min sans guide humain.
- [ ] Reconnaissance fiable des 4 commandes (plané, direction, battement, distance).
- [ ] Collisions et bonus déclenchés correctement.
- [ ] Affichage score & record persistant fonctionnel.
- [ ] Reset entre joueurs < 5 secondes, sans intervention technique.
- [ ] Stabilité hardware/software sur +20 parties consécutives sans crash/latence.
