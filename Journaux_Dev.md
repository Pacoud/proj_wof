# COMMIT  b7cabff7ed2690a999eb44053939ef7ce7dbca74 -- #   
pathfinding (arbres de graphes), le ravitaillement traverse plusieurs provinces 

- Recherche automatique d’un chemin logistique entre un dépôt et une division.
- Parcours du réseau de provinces à l’aide d’un algorithme de type BFS.
- Représentation d’un chemin logistique complet avec plusieurs `SupplyRoute`.
- Calcul de la capacité maximale d’un chemin selon son maillon le plus faible.
- Consommation de la capacité de chaque route traversée lors du transfert de carburant.
- Partage de la capacité d’un même chemin entre plusieurs divisions.
- Gestion du cas où aucun chemin logistique n’existe.


# COMMIT  47da8f214851e333d5e22797109ae7498c3f69b0 # 
-recherche du chemin offrant la meilleure capacité logistique ;
-prise en compte du maillon le plus faible de chaque trajet ;
-utilisation des capacités restantes pendant le tick, et non uniquement des capacités nominales ;
-possibilité d’utiliser automatiquement un chemin alternatif lorsqu’une branche du réseau est saturée ;
-partage des capacités du réseau entre plusieurs divisions ;
-ajout de tests validant le choix du meilleur trajet et l’utilisation d’itinéraires alternatifs.


# COMMIT e5a4805fba5bcf83d65ab0a474b49c0cebcab0de # 

-Le réseau logistique ne repose plus uniquement sur des liaisons abstraites définies par une capacité arbitraire.
Les SupplyRoute possèdent désormais un type d’infrastructure et un niveau, à partir desquels leur capacité de transport est calculée automatiquement.

ajout de InfrastructureType ;
distinction entre route et voie ferrée ;
ajout d’un niveau d’infrastructure ;
calcul automatique de la capacité à partir du type et du niveau ;
conservation de la compatibilité avec l’algorithme de recherche du meilleur chemin ;
ajout de tests vérifiant qu’une voie ferrée transporte davantage qu’une route de même niveau ;
ajout de tests vérifiant qu’une infrastructure de niveau supérieur possède une capacité plus élevée.


# COMMIT 2c3930d3553cd4600083ad2f3e8b4c0d079ee734  #

L'infrastructure a été refactorisée afin de ne plus appartenir exclusivement au système logistique.

La classe `SupplyRoute` a été remplacée par une représentation plus générale appelée `InfrastructureLink`. Une même liaison d'infrastructure peut désormais être utilisée par plusieurs systèmes de simulation.

### Refactoring

- remplacement de `SupplyRoute` par `InfrastructureLink` ;
- déplacement des types d'infrastructure hors du module logistique ;
- remplacement de `FuelCapacityPerHour` par `TransportCapacityPerHour` ;
- adaptation du réseau logistique et de l'algorithme de widest path aux nouvelles infrastructures ;
- remplacement des anciennes collections de routes par des collections de liens d'infrastructure.

### Déplacement militaire

Le temps de déplacement d'une division entre deux provinces voisines dépend désormais de la qualité de l'infrastructure routière.

Valeurs provisoires du prototype :

- aucune route : 6 heures ;
- route niveau 1 : 5 heures ;
- route niveau 2 : 4 heures ;
- route niveau 3 : 3 heures.

Les valeurs sont actuellement arbitraires et servent uniquement à valider le comportement de la simulation.

Les voies ferrées n'accélèrent pas encore le déplacement militaire normal. Elles sont actuellement utilisées principalement par le système logistique et feront ultérieurement l'objet d'un système spécifique de transport stratégique.

### Architecture

Une même infrastructure influence désormais plusieurs systèmes :

InfrastructureLink
├── Logistics
└── Military Movement

Cette évolution permet d'éviter de maintenir des représentations différentes d'une même infrastructure selon le système qui l'utilise.

### Tests ajoutés

- vérification qu'une route de niveau supérieur réduit le temps de déplacement ;
- vérification du temps de déplacement sans route ;
- vérification qu'une voie ferrée n'accélère pas le mouvement normal ;
- test d'intégration confirmant que `SimulationEngine` utilise l'infrastructure pour déterminer la durée réelle du déplacement.


## FIN COMMIT ##

# COMMIT 3e4775dc26bb00d8a0533110e712325739d21adc # 
## Itération — Prise en compte du terrain dans les déplacements

Les provinces possèdent désormais un type de terrain influençant la durée des déplacements militaires.

### Terrains implémentés

Les premiers types de terrain disponibles sont :

- Plains
- Forest
- Hills
- Mountain
- Marsh
- Desert
- Urban

Une province est considérée comme `Plains` par défaut afin de conserver la compatibilité avec les scénarios et tests précédents.

### Calcul du déplacement

La durée d'un déplacement dépend désormais de deux facteurs :

1. la qualité de l'infrastructure routière ;
2. le terrain de la province de destination.

La durée obtenue grâce à l'infrastructure est multipliée par un coefficient propre au terrain.

Valeurs provisoires :

Plains   : x1.00
Urban    : x1.15
Desert   : x1.20
Forest   : x1.30
Hills    : x1.40
Marsh    : x1.60
Mountain : x2.00

## Fin COMMIT ##

# COMMIT a542ebd7712a61e343e9f466b790d23d10bd217a  # 
## Itération — Types de divisions et profils de mobilité

Les divisions disposent désormais d'un type influençant leur vitesse de déplacement et leur sensibilité aux différents terrains.

### Types actuellement implémentés

- `Infantry`
- `Motorized`
- `Armored`

Le type `Infantry` est utilisé par défaut afin de conserver la compatibilité avec les scénarios et tests précédents.

### Mobilité

Chaque type de division possède désormais un multiplicateur de mobilité de base.

Valeurs provisoires :

Infantry  : x1.00
Motorized : x0.60
Armored   : x0.70

Un coefficient inférieur permet de réduire la durée du déplacement.

Les unités motorisées et blindées bénéficient donc d'une mobilité supérieure sur les terrains favorables.

### Interaction avec le terrain

Les pénalités de terrain dépendent maintenant du type de division.

Les formations motorisées et blindées sont particulièrement pénalisées par les terrains difficiles tels que :

- les forêts ;
- les collines ;
- les marais ;
- les montagnes.

Une formation blindée peut ainsi être plus rapide qu'une division d'infanterie en plaine mais devenir plus lente dans un environnement montagneux.

### Architecture

Le calcul de déplacement prend désormais en compte :

- l'infrastructure ;
- le terrain ;
- le type de division.

Le `SimulationEngine` transmet le type de la division au `MovementSystem`, qui reste responsable du calcul de la durée.

### Tests ajoutés

- vérification qu'une division motorisée se déplace plus rapidement qu'une division d'infanterie sur terrain favorable ;
- vérification de la forte pénalité des formations blindées en montagne ;
- vérification que les divisions sans type explicitement indiqué sont considérées comme de l'infanterie ;
- test d'intégration vérifiant que le `SimulationEngine` utilise correctement le type de division.


## FIN COMMIT ##







# COMMIT a5eef014051efbcabce385f4e88b8666ad6979c8 #
## Itération — Calcul dynamique de la consommation de carburant

Le coût en carburant des déplacements militaires n'est désormais plus représenté par une valeur fixe.

Un déplacement génère maintenant un `MovementPlan` contenant :

- sa durée estimée ;
- son coût total en carburant.

### Facteurs pris en compte

La consommation dépend actuellement de :

- la durée du déplacement ;
- le type de division ;
- le terrain de destination ;
- la qualité de l'infrastructure routière.

Le calcul utilisé par le prototype est :

FuelCost =
BaseFuelConsumptionPerHour
× MovementDuration
× TerrainFuelMultiplier
× InfrastructureFuelMultiplier

### Types de divisions

Les consommations horaires provisoires sont :

Infantry  : 1.0 unité/h
Motorized : 2.5 unités/h
Armored   : 4.0 unités/h

Les formations motorisées et blindées nécessitent donc davantage de carburant pour leurs déplacements.

### Effet du terrain

Les terrains difficiles augmentent également la consommation.

Valeurs provisoires :

Plains   : x1.00
Urban    : x1.10
Desert   : x1.15
Forest   : x1.20
Hills    : x1.25
Marsh    : x1.40
Mountain : x1.50

Le terrain influence à la fois la durée du trajet et son coût énergétique.

### Effet des routes

La qualité des routes influence désormais également l'efficacité énergétique :

aucune route : x1.25
Road L1      : x1.10
Road L2      : x0.95
Road L3      : x0.85

Une infrastructure routière de meilleure qualité réduit donc le temps de déplacement ainsi que la consommation de carburant.

### MovementPlan

Une nouvelle structure `MovementPlan` rassemble les informations nécessaires à l'exécution d'un mouvement :

MovementPlan
├── DurationHours
└── FuelCost

Le `MovementSystem` est responsable de la création du plan tandis que la division vérifie uniquement qu'elle dispose des ressources nécessaires pour l'exécuter.

### Intégration avec la logistique

Le carburant transporté par le réseau logistique alimente directement les réserves utilisées par les divisions lors de leurs mouvements.

Une division ne disposant pas de suffisamment de carburant ne peut plus commencer son déplacement.

Cette évolution relie donc directement le système logistique au système de mouvement militaire.

### Tests ajoutés

- comparaison de la consommation entre infanterie, motorisée et blindée ;
- vérification de l'augmentation de consommation en terrain difficile ;
- vérification de la réduction de consommation offerte par une meilleure route ;
- vérification qu'une division ne peut pas commencer un mouvement sans suffisamment de carburant ;
- maintien des règles précédentes de durée de déplacement.



# COMMIT 950d5d522955b739760fa020abcdf76527e6bd88 # 

## Itération — Consommation progressive et contrôle du déplacement

Le système de mouvement a été modifié afin que le carburant ne soit plus consommé intégralement au moment où un ordre est donné.

La consommation est désormais appliquée progressivement à chaque tick de simulation.

### MovementPlan

Le `MovementPlan` contient maintenant :

- la durée totale du déplacement ;
- la consommation de carburant par heure.

Il expose également une estimation du coût total du trajet.

MovementPlan
├── DurationHours
├── FuelPerHour
└── EstimatedFuelCost

Le coût estimé n'est plus retiré au départ du mouvement.

### Consommation par tick

Lorsqu'une division est en déplacement, chaque tick :

1. vérifie que la division dispose de suffisamment de carburant ;
2. retire la consommation correspondant à une heure de déplacement ;
3. réduit le temps restant du trajet d'une heure.

Le carburant n'est donc consommé que lorsque la division progresse réellement.

### Arrêt manuel

Une division en mouvement peut désormais recevoir un ordre d'arrêt.

Lorsqu'elle est stoppée :

- sa progression est conservée ;
- son temps restant ne diminue plus ;
- elle ne consomme plus de carburant ;
- son ordre de déplacement reste actif mais en pause.

Une commande de reprise permet ensuite de continuer le même trajet sans perdre la progression déjà effectuée.

### Panne de carburant

Une division n'a plus besoin de posséder au départ la totalité du carburant nécessaire au trajet.

Elle doit uniquement disposer de suffisamment de carburant pour commencer le premier tick.

Si son carburant devient insuffisant pendant le déplacement :

- la progression s'arrête automatiquement ;
- le carburant restant n'est pas consommé ;
- l'ordre est placé en pause ;
- la division reste considérée comme étant en transit.

Cette évolution permet désormais à un problème logistique de provoquer directement l'arrêt d'une opération militaire.

### États de déplacement

Deux états sont maintenant distingués :

`IsMoving`
: la division progresse actuellement et consomme du carburant.

`IsInTransit`
: la division possède encore un ordre de déplacement en cours, qu'elle soit en mouvement ou en pause.

Cette distinction empêche notamment une division arrêtée en transit d'être considérée comme stationnée normalement dans sa province d'origine.

### Intégration avec le ravitaillement

Le réseau logistique ignore désormais toutes les divisions dont `IsInTransit` est vrai.

Une division arrêtée au milieu d'un trajet ne peut donc pas être ravitaillée comme si elle était toujours stationnée dans sa province d'origine.

Le ravitaillement des unités en transit fera l'objet d'une évolution ultérieure.

### Tests ajoutés

- vérification qu'aucun carburant n'est consommé au moment où l'ordre de déplacement est donné ;
- vérification de la consommation à chaque tick ;
- vérification qu'une division stoppée ne consomme plus de carburant ;
- vérification qu'une division stoppée ne progresse plus ;
- vérification de la reprise d'un déplacement interrompu ;
- vérification de l'arrêt automatique lorsqu'une division manque de carburant pendant son trajet.

# COMMIT 08a9b5413369d3950252867fdcc775bef419c43e # 
## Itération — Représentation explicite des divisions en transit

Le système de mouvement a été refactorisé afin qu'une division en déplacement ne soit plus considérée comme étant toujours présente dans sa province d'origine.

Une division possède désormais deux états géographiques mutuellement exclusifs :

- stationnée dans une province ;
- en transit sur une liaison entre deux provinces.

### TransitState

L'ancien `MovementOrder` est remplacé par un `TransitState` représentant la position physique de la division pendant son déplacement.

Un état de transit contient notamment :

- la province d'origine ;
- la province de destination ;
- la durée totale ;
- le temps écoulé ;
- le temps restant ;
- la progression entre 0 et 1 ;
- la consommation horaire de carburant ;
- l'état de pause.

Exemple :

Origin = A
Destination = B
Progress = 0.40

représente une division ayant parcouru 40 % de la liaison entre A et B.

### Position des divisions

Une division stationnée possède :

CurrentProvince != null
Transit = null

Une division en déplacement possède :

CurrentProvince = null
Transit != null

Une division n'est donc jamais simultanément considérée comme étant dans une province et sur une liaison.

### Progression

La progression est calculée à partir du temps déjà parcouru :

Progress = ElapsedHours / TotalHours

Une division arrêtée conserve sa progression exacte et peut reprendre son déplacement au même endroit.

### Arrivée

Lorsque la progression atteint 100 % :

- l'état de transit est supprimé ;
- la province de destination devient `CurrentProvince`.

La division redevient alors une unité stationnée.

### Ordres donnés pendant un transit

Une division déjà en transit ne peut pas bifurquer directement vers une troisième province.

Si elle se déplace de A vers B et reçoit un ordre vers C :

A -> B -> C

l'ordre vers C est enregistré comme destination en attente.

La division :

1. termine la liaison A -> B ;
2. atteint B ;
3. commence automatiquement le trajet B -> C.

Pour cette version du prototype, C doit être directement voisine de B. Le calcul d'itinéraires sur plusieurs provinces sera implémenté ultérieurement.

### Intégration avec la logistique

Une division en transit n'est plus considérée comme présente dans sa province d'origine.

Elle ne peut donc pas utiliser le ravitaillement local d'une province tant que son déplacement n'est pas terminé.

### Préparation des futurs combats

La représentation explicite de la progression permet désormais de connaître la position relative de plusieurs divisions présentes sur une même liaison.

Cette architecture permettra ultérieurement de détecter :

- les rencontres entre deux forces se déplaçant en sens opposé ;
- les unités arrêtées sur une liaison ;
- les combats de rencontre ;
- les interceptions pendant un déplacement.

Aucun système de combat n'est encore implémenté.

### Tests ajoutés

- vérification qu'une division quitte réellement sa province au début du mouvement ;
- vérification de la progression à chaque tick ;
- vérification de l'arrivée correcte dans la province de destination ;
- vérification de la conservation de la progression lors d'un arrêt ;
- vérification de l'enregistrement d'une destination future ;
- vérification de l'enchaînement automatique de deux déplacements successifs.

# COMMIT 6755774d2781e54e8a01ad2ebbdc6a2cc7f81ebf #

## Itération — Pathfinding militaire et itinéraires explicites

Le système de mouvement permet désormais de donner des ordres vers des provinces non directement adjacentes.

Le moteur construit automatiquement une succession de déplacements entre provinces voisines.

### Pathfinding automatique

Un `MilitaryPathfinder` a été ajouté afin de rechercher l'itinéraire le plus rapide entre deux provinces.

L'algorithme utilisé est actuellement Dijkstra.

Contrairement à une recherche basée uniquement sur le nombre de provinces traversées, le coût de chaque liaison correspond au temps réel de déplacement calculé par le `MovementSystem`.

Le pathfinding prend donc indirectement en compte :

- le type de division ;
- le terrain ;
- la qualité des infrastructures routières.

Un trajet contenant davantage de provinces peut être sélectionné s'il est plus rapide qu'un itinéraire plus direct traversant un terrain difficile.

### Exemple

Deux itinéraires sont disponibles :

A -> B -> D
15 heures

A -> C -> E -> D
9 heures

Le moteur sélectionne automatiquement :

A -> C -> E -> D

même si ce trajet traverse davantage de provinces.

### MovementRoute

Une division peut désormais conserver une file de provinces à traverser.

Exemple :

A -> B -> C -> D

Pendant le transit A -> B, les destinations restantes sont :

C -> D

À l'arrivée en B, le moteur peut automatiquement commencer le segment suivant.

### Ordres reçus pendant un transit

Lorsqu'une division reçoit une nouvelle destination pendant qu'elle se trouve sur une liaison, son trajet actuel n'est pas interrompu.

Le calcul du nouvel itinéraire commence depuis la province qu'elle est actuellement en train de rejoindre.

Une division située à 40 % de A -> B doit donc atteindre B avant de pouvoir emprunter une nouvelle branche du réseau.

### Itinéraires explicites

Le moteur accepte également un itinéraire entièrement défini par le joueur.

Exemple :

A -> C -> F -> G -> D

Dans ce mode, aucun pathfinding automatique n'est effectué : le moteur respecte la succession de provinces fournie.

Chaque paire de provinces successives doit être adjacente.

Cette fonctionnalité est conçue pour permettre ultérieurement à l'interface graphique de laisser le joueur tracer manuellement le trajet d'une division sur la carte.


# COMMIT 3fdf17480ebbdf0b9501cb1382a4e8e07c22e16b #
## Itération — Appartenance et contrôle territorial des provinces

Les provinces disposent désormais d'une identité territoriale distinguant leur propriétaire de la puissance qui les contrôle militairement.

### Country

Une première entité `Country` a été introduite.

Elle contient actuellement uniquement :

- un identifiant ;
- un nom.

Les systèmes politiques, économiques et diplomatiques seront ajoutés ultérieurement.

### Propriétaire d'une province

Chaque province peut posséder un `Owner`.

Le propriétaire représente l'appartenance politique ou historique du territoire.

Cette valeur n'est pas automatiquement modifiée lors d'une occupation militaire.

### Contrôleur d'une province

Chaque province possède également un `Controller`.

Le contrôleur représente la puissance exerçant actuellement le contrôle militaire du territoire.

Lors de la création d'une province, le contrôleur est par défaut identique au propriétaire.

### Occupation

Lorsqu'une division appartenant à un autre pays termine son déplacement dans une province, le contrôle de celle-ci est transféré au pays de la division.

Exemple :

Avant :

Owner      = Germany
Controller = Germany

Après l'arrivée d'une division française :

Owner      = Germany
Controller = France

Le territoire est donc considéré comme occupé sans modifier son propriétaire politique.

### Nationalité des divisions

Les divisions peuvent désormais appartenir à un `Country`.

Cette information permettra progressivement de déterminer :

- les territoires amis ;
- les territoires ennemis ;
- les droits de passage ;
- les engagements militaires ;
- les changements de contrôle territorial.

### Intégration avec le mouvement

Le `SimulationEngine` détecte désormais lorsqu'une division termine un transit.

À son arrivée, le moteur vérifie le contrôleur actuel de la province et met à jour le contrôle territorial si nécessaire.

La capture n'a lieu qu'à l'arrivée effective dans la province et non au début du déplacement.

### Tests ajoutés

- vérification que le contrôleur initial correspond au propriétaire ;
- vérification qu'un changement de contrôle ne modifie pas le propriétaire ;
- vérification qu'une division arrivant dans une province ennemie en prend le contrôle ;
- vérification qu'un déplacement entre provinces déjà contrôlées par le même pays ne modifie pas leur appartenance.


# COMMIT 2e4617c163cceb18caf3548adbac98b69bb0595d #

## Itération — États de guerre et restrictions territoriales

Le système territorial prend désormais en compte les relations de guerre entre pays.

Les provinces ne sont plus considérées comme librement accessibles par toutes les divisions.

### DiplomacySystem

Un premier système diplomatique a été introduit.

Il permet actuellement de gérer :

- la déclaration de guerre ;
- la fin d'une guerre ;
- la vérification de l'état de guerre entre deux pays.

Les guerres sont symétriques :

si France est en guerre avec Germany, Germany est également en guerre avec France.

### Définition d'un territoire ennemi

L'hostilité d'une province dépend de son `Controller`.

Une province est considérée comme ennemie lorsqu'elle est contrôlée par un pays actuellement en guerre avec le pays auquel appartient la division.

Le `Owner` n'est donc pas utilisé pour déterminer l'accès militaire immédiat.

### Accès territorial

Les premières règles d'accès militaire sont :

- territoire contrôlé par son propre pays : accessible ;
- territoire non contrôlé : accessible ;
- territoire contrôlé par un pays neutre : interdit ;
- territoire ennemi : accessible uniquement comme objectif final d'une offensive.

### Restriction du pathfinding

Le pathfinding militaire ne peut pas utiliser une province ennemie comme simple province intermédiaire.

Exemple :

France       Germany       Germany

A ---------- B ----------- C

Même en état de guerre, un ordre direct de A vers C n'est pas encore autorisé car B est une province ennemie non contrôlée.

Le joueur doit d'abord capturer B :

A -> B

puis, une fois :

Controller(B) = France

un nouvel ordre vers C devient possible.

Cette règle empêche le pathfinding de planifier des déplacements profonds à travers un territoire ennemi comme s'il était déjà sécurisé.

### Territoires neutres

Une division ne peut pas entrer dans une province contrôlée par un pays avec lequel elle n'est pas en guerre.

Cela empêche notamment le pathfinding automatique et les itinéraires explicites de traverser un pays neutre.

Les alliances et droits de passage ne sont pas encore implémentés.

### Itinéraires explicites

Les itinéraires tracés manuellement par le joueur utilisent désormais les mêmes règles d'accès territorial que le pathfinding automatique.

Un itinéraire manuel ne peut donc pas être utilisé pour contourner les restrictions politiques ou militaires du moteur.

### Capture territoriale

Une division ne prend désormais le contrôle d'une province étrangère que si son pays est en guerre avec le pays qui la contrôle.

La distinction entre `Owner` et `Controller` reste conservée.

Exemple :

avant :

Owner      = Germany
Controller = Germany

après occupation française :

Owner      = Germany
Controller = France

### Tests ajoutés

- vérification de la symétrie d'un état de guerre ;
- vérification qu'un territoire neutre est inaccessible ;
- vérification qu'une province ennemie peut être ciblée pendant une guerre ;
- vérification qu'une province ennemie ne peut pas être utilisée comme étape intermédiaire ;
- vérification que la capture territoriale nécessite un état de guerre ;
- vérification que les itinéraires explicites respectent les restrictions territoriales.


# COMMIT 9be38eff2f74877cebe9297ec2cef1a948bd9595 # 

## Itération — Détection des engagements militaires et consommation de combat

Le moteur peut désormais détecter un contact hostile entre divisions et créer un état d'engagement militaire.

Cette itération introduit la première frontière entre les systèmes de mouvement et de combat, sans encore calculer les pertes ou déterminer un vainqueur.

### États opérationnels

Les divisions disposent désormais d'un état opérationnel dérivé de leur situation :

- `Stationary`
- `Moving`
- `Paused`
- `Engaged`

Lorsqu'un contact hostile est détecté, les divisions concernées passent automatiquement dans l'état `Engaged`.

### Engagement

Une nouvelle entité `Engagement` représente un contact militaire actif.

Un engagement contient actuellement :

- un identifiant ;
- l'heure de début ;
- les divisions participantes ;
- son emplacement ;
- son état actif.

Deux types de localisation sont actuellement possibles :

- une province ;
- une liaison entre deux provinces.

Aucune résolution de combat n'est encore effectuée.

### Défense d'une province

Lorsqu'une division arrive dans une province contenant une force appartenant à un pays ennemi :

1. la capture automatique de la province est interrompue ;
2. un engagement est créé ;
3. l'attaquant et le défenseur passent dans l'état `Engaged`.

Le contrôle territorial reste donc inchangé tant que l'engagement n'est pas résolu.

### Rencontre sur une liaison

Deux divisions ennemies se déplaçant en sens opposé sur la même liaison peuvent désormais se rencontrer pendant un tick.

La détection utilise leur progression précédente et actuelle afin de déterminer si leurs trajectoires se sont croisées.

Exemple :

Tick précédent :

France = 25 %
Germany = 75 %

Tick suivant :

France = 50 %
Germany = 50 %

Un engagement est alors créé sur la liaison.

### Suspension du mouvement

Lorsqu'une division entre en combat :

- son `TransitState` est conservé ;
- sa progression est suspendue ;
- son itinéraire futur n'est pas exécuté ;
- sa position relative sur la liaison est conservée.

Cette architecture permettra ultérieurement de reprendre, annuler ou transformer le mouvement après la résolution du combat.

### Consommation de carburant au combat

L'arrêt du mouvement ne signifie pas l'arrêt de la consommation de carburant.

Les divisions engagées utilisent désormais une consommation horaire de combat provisoire :

Infantry  : 0.8 unité/h
Motorized : 3.0 unités/h
Armored   : 5.0 unités/h

Cette consommation remplace la consommation de déplacement pendant l'engagement.

### Manque de carburant pendant un combat

Une division ne quitte pas un engagement lorsqu'elle manque de carburant.

Si son stock est insuffisant :

- elle consomme le carburant restant ;
- son stock peut atteindre zéro ;
- l'engagement continue.

Les pénalités militaires liées au manque de carburant seront introduites lors de la résolution réelle des combats.

### Logistique

Les divisions engagées sont temporairement exclues du ravitaillement logistique normal.

Un futur système pourra différencier :

- ravitaillement normal ;
- ravitaillement d'une unité en transit ;
- ravitaillement d'une formation engagée au combat.

### Intégration avec le SimulationEngine

Chaque tick traite maintenant notamment :

1. la consommation de carburant des engagements déjà actifs ;
2. les déplacements ;
3. la détection des rencontres sur les liaisons ;
4. la détection des forces hostiles dans une même province ;
5. la capture des provinces uniquement en l'absence d'opposition ;
6. le ravitaillement ;
7. la reprise éventuelle des itinéraires non engagés.

### Tests ajoutés

- vérification qu'un défenseur ennemi empêche la capture automatique d'une province ;
- vérification de la création d'un engagement dans une province contestée ;
- vérification de la rencontre de deux divisions ennemies sur une liaison ;
- vérification de l'arrêt de la progression pendant un engagement ;
- vérification de la consommation horaire de carburant au combat ;
- vérification qu'une division reste engagée même lorsque son carburant atteint zéro.


# COMMIT bd02cf1e0dd5d92027807c9a2a4c11fdcf9f1964 # 
## Itération — Organisation et première résolution des engagements

Les engagements militaires peuvent désormais évoluer et se terminer grâce à l'introduction d'une valeur d'organisation pour chaque division.

Cette itération constitue la première forme de résolution des combats du prototype.

### Organisation

Chaque division possède désormais :

- une organisation maximale ;
- une organisation actuelle.

Par défaut :

MaxOrganization = 100
Organization = 100

L'organisation représente de manière abstraite la cohésion, la coordination et la capacité immédiate d'une formation à continuer le combat.

Une division dont l'organisation atteint zéro devient `Broken`.

### Nouvel état opérationnel

Un nouvel état a été ajouté :

`Broken`

Une division brisée :

- n'est plus capable de poursuivre normalement le combat ;
- ne peut plus recevoir d'ordre de mouvement ;
- ne peut pas reprendre un mouvement suspendu.

La retraite des formations brisées sera implémentée ultérieurement.

### Pression de combat

Chaque type de division génère pour l'instant une pression organisationnelle abstraite.

Valeurs provisoires :

Infantry  : 6.0
Motorized : 7.5
Armored   : 10.0

Ces valeurs ne représentent pas encore des données historiques et servent uniquement à construire le premier modèle de résolution.

### Influence du carburant

La puissance de combat dépend désormais de la quantité de carburant réellement disponible pendant le tick.

Une division qui reçoit tout le carburant nécessaire combat à pleine efficacité.

Une division manquant de carburant voit son efficacité diminuer.

Efficacité minimale provisoire sans carburant :

Infantry  : 85 %
Motorized : 50 %
Armored   : 25 %

Une formation blindée sans carburant reste donc engagée mais perd une grande partie de sa capacité offensive.

### Résolution simultanée

Les pressions de combat de toutes les divisions sont calculées avant l'application des pertes d'organisation.

Cela évite qu'un camp obtienne artificiellement un avantage uniquement parce qu'il est traité en premier dans la boucle de simulation.

### Supériorité numérique

La pression totale d'un camp est répartie entre les formations adverses.

Une supériorité numérique commence ainsi à produire naturellement un avantage organisationnel.

Exemple simplifié :

2 divisions à 6 points de pression
contre
1 division à 6 points.

Le défenseur reçoit 12 points de pression tandis que les deux divisions adverses se partagent les 6 points opposés.

### Fin d'un engagement

Après chaque tick de combat, le moteur vérifie quels pays disposent encore d'au moins une division dont l'organisation est supérieure à zéro.

Si un seul pays reste capable de combattre :

- l'engagement se termine ;
- ce pays est enregistré comme vainqueur ;
- les participants quittent l'état `Engaged`.

Si aucune formation ne reste opérationnelle, l'engagement peut se terminer sans vainqueur.

### Itinéraires après combat

Pour cette première version, les itinéraires planifiés des participants sont supprimés lors de la fin d'un engagement.

Aucune division ne reprend donc automatiquement son ancienne progression après une bataille.

Le comportement post-combat sera traité avec les futurs systèmes de retraite et de poursuite.

### Intégration avec le carburant

Un tick d'engagement traite maintenant :

1. la consommation de carburant de combat ;
2. l'efficacité résultante de chaque division ;
3. la pression de combat ;
4. les pertes d'organisation ;
5. la détection des formations brisées ;
6. la résolution éventuelle de l'engagement.

### Tests ajoutés

- vérification de la perte d'organisation pendant un engagement ;
- vérification qu'une formation blindée disposant de carburant inflige davantage de pression qu'une formation à sec ;
- vérification du passage à l'état `Broken` lorsque l'organisation atteint zéro ;
- vérification de la fin d'un engagement lorsqu'un seul camp reste opérationnel ;
- vérification qu'une division brisée ne peut plus recevoir d'ordre de mouvement.



# COMMIT 5b6a0e8a5a82eb70724dae27ffbb0633535ba7c5 # 
## Itération — Retraite automatique des divisions brisées

Les divisions dont l'organisation atteint zéro ne restent désormais plus indéfiniment sur le champ de bataille.

Une formation brisée tente automatiquement de se replier vers une province amie située à l'arrière du front.

### État Retreating

Un nouvel état opérationnel `Retreating` a été introduit.

Une division en retraite :

- reste considérée comme `Broken` ;
- ne peut pas recevoir d'ordres de mouvement normaux ;
- suit automatiquement un itinéraire calculé par le moteur ;
- ne peut pas interrompre volontairement sa retraite.

Une fois arrivée à destination, elle quitte l'état `Retreating` mais reste `Broken` tant que son organisation n'a pas récupéré.

### Recherche d'une province de retraite

Un `RetreatPathfinder` recherche automatiquement une destination valide.

Une province de retraite doit actuellement :

- être contrôlée par le pays de la division ;
- ne pas contenir de division ennemie capable de combattre.

Le moteur préfère ensuite une province qui n'est adjacente à aucune province contenant une force ennemie capable de combattre.

Parmi les destinations répondant à ce critère, la province accessible la plus proche est sélectionnée.

### Fallback

Si aucune province réellement sûre n'existe, le moteur sélectionne la province amie valide la plus proche.

Une division ne reste donc pas sur le champ de bataille simplement parce qu'aucune destination parfaite n'est disponible.

### Exemple

Front :

Enemy
  |
Battlefield -- Rear 1 -- Rear 2

`Rear 1` est contrôlée par le pays vaincu mais reste adjacente à la force ennemie présente sur le champ de bataille.

`Rear 2` n'est plus adjacente à l'ennemi.

Le chemin sélectionné est donc :

Battlefield -> Rear 1 -> Rear 2

### Mouvement forcé

Une retraite utilise le système de déplacement existant mais constitue un mouvement forcé.

Une division brisée peut donc effectuer une retraite alors qu'elle ne peut plus recevoir d'ordre de déplacement normal.

### Carburant pendant la retraite

Une division tente de consommer le carburant normalement requis pendant son repli.

Contrairement au mouvement normal, une quantité insuffisante de carburant ne bloque pas complètement la retraite.

Cette abstraction représente provisoirement :

- l'abandon de matériel ;
- le remorquage ;
- la marche à pied ;
- les mouvements d'urgence.

Les conséquences matérielles d'une retraite sans carburant seront modélisées ultérieurement.

### Conséquences territoriales d'une victoire

Lorsqu'un engagement dans une province se termine avec un vainqueur, le contrôle de la province est désormais transféré au pays vainqueur.

Les formations brisées du camp perdant commencent ensuite leur repli vers l'arrière.

### Sélection des routes

La retraite utilise pour l'instant une recherche basée principalement sur :

1. la sécurité de la destination ;
2. la distance en nombre de provinces.

La qualité des routes n'est pas encore utilisée comme critère principal de sélection d'une destination de retraite.

### Tests ajoutés

- vérification qu'une province non adjacente à l'ennemi est préférée ;
- vérification de l'utilisation d'une province de fallback lorsqu'aucune destination parfaitement sûre n'existe ;
- vérification du déclenchement automatique d'une retraite après une défaite ;
- vérification de l'arrivée d'une division brisée dans une province arrière ;
- vérification du transfert de contrôle territorial au vainqueur.

# COMMIT 437194afa3ede04370d9e9ea8708c4fe85274a8c # 
## Itération — Retraite après un engagement sur une liaison

Le système de retraite prend désormais en charge les divisions brisées lors d'un combat ayant lieu entre deux provinces.

Une division vaincue au milieu d'une liaison n'est plus téléportée vers une province.

### Inversion du TransitState

`TransitState` peut désormais inverser son origine et sa destination tout en conservant exactement la position physique de la division.

Exemple :

A -> B
Progress = 40 %

devient :

B -> A
Progress = 60 %

La division reste au même emplacement sur la liaison.

Pour un trajet de cinq heures :

avant :

ElapsedHours   = 2
RemainingHours = 3

après inversion :

ElapsedHours   = 3
RemainingHours = 2

La division met donc exactement deux heures à revenir vers la province qu'elle avait quittée.

### Phases de retraite

La retraite distingue maintenant plusieurs phases :

- `None`
- `ReturningToOrigin`
- `MovingToSafety`

Lorsqu'une division est brisée lors d'un engagement sur une liaison, elle passe d'abord en :

`ReturningToOrigin`

Elle fait demi-tour sur la liaison et retourne vers sa province de départ.

Une fois cette province atteinte, le `RetreatPathfinder` recherche une province arrière sûre et la division passe en :

`MovingToSafety`

### Vainqueur d'un combat sur une liaison

Une division victorieuse non brisée reprend le transit qui avait été suspendu par l'engagement.

Le comportement devient donc :

Winner:
resume current transit

Loser:
reverse current transit
-> return to origin
-> retreat toward safe rear

Les itinéraires futurs restent supprimés après un engagement ; seul le segment interrompu du vainqueur est repris.

### Absence de téléportation

Une division vaincue ne change jamais instantanément de province.

Sa position intermédiaire est conservée grâce au progrès du `TransitState`.

La retraite utilise donc le même système temporel de déplacement que les mouvements militaires ordinaires.

### Capture territoriale

Une division brisée ou en retraite ne peut pas capturer une province simplement parce qu'elle y arrive pendant son repli.

Les changements de contrôle territorial restent réservés aux unités capables d'occuper normalement le territoire.

### Interaction avec le système de retraite existant

Après le retour à la province d'origine :

1. le moteur détecte la fin du transit inverse ;
2. le `RetreatPathfinder` recherche une destination arrière ;
3. un itinéraire de retraite est créé ;
4. la division poursuit automatiquement son repli ;
5. elle quitte l'état `Retreating` une fois arrivée à sa destination finale.

### Tests ajoutés

- vérification que l'inversion d'un `TransitState` conserve la position physique ;
- vérification de l'inversion de l'origine et de la destination ;
- vérification qu'une division brisée sur une liaison retourne vers sa province de départ ;
- vérification que le vainqueur reprend son transit interrompu ;
- vérification du passage de `ReturningToOrigin` vers `MovingToSafety`.

# COMMIT 29ec5fbf1bdff88e094ef4cc2d70b9f754044fce # 

## Itération — Récupération progressive de l'organisation

Les divisions peuvent désormais récupérer progressivement leur organisation après un combat.

Cette évolution permet à une formation brisée de redevenir opérationnelle après une période de repos dans une zone arrière sûre.


### État Broken
`Broken` ne représente pas directement un faible niveau d'organisation.

Il représente une formation ayant rompu au combat et se trouvant dans un état de déroute.

Une division devient `Broken` lorsque son organisation atteint zéro pendant un engagement.

Elle reste `Broken` pendant sa retraite.

Une fois sa retraite terminée dans une province arrière valide, elle quitte immédiatement l'état `Broken`, même si son niveau d'organisation reste très faible.

La faiblesse d'une formation récemment retraitée n'est donc pas représentée par une interdiction artificielle de combattre, mais directement par son faible niveau d'organisation.

Une division remise en ligne trop rapidement pourra ainsi être engagée normalement mais risque de rompre presque immédiatement si elle subit une nouvelle pression ennemie.

### Conditions de récupération

Une division ne récupère son organisation que si :

- elle est stationnée dans une province ;
- elle n'est pas engagée ;
- elle n'est pas en transit ;
- elle n'est pas en retraite ;
- la province est contrôlée par son propre pays ;
- aucune force ennemie capable de combattre n'est présente dans la province ou dans une province adjacente.

Une formation située directement au contact du front ne peut donc pas se réorganiser normalement.

### Tests ajoutés

- récupération progressive dans une province amie sûre ;
- maintien de l'état `Broken` sous le seuil de réorganisation ;
- sortie de l'état `Broken` lorsque le seuil est atteint ;
- absence de récupération lorsqu'une force ennemie se trouve à proximité ;
- absence de récupération pendant un déplacement ;
- limitation de l'organisation à sa valeur maximale.

# COMMIT 72d38c1a27ee5e32c0e5cbdd281a790aa579bf1f # 
## Itération — Terrain des engagements et localisation des combats de rencontre

Le système de combat distingue désormais explicitement les combats ayant lieu dans une province des rencontres entre forces se déplaçant sur une liaison.

### Types d'engagement

Deux types d'engagement sont actuellement représentés :

- `ProvinceBattle`
- `MeetingEngagement`

Un `ProvinceBattle` correspond à un combat ayant lieu directement dans une province.

Un `MeetingEngagement` correspond à la rencontre de deux formations sur une liaison entre deux provinces.

Aucune distinction attaquant/défenseur n'est encore appliquée aux combats de rencontre.

### Terrain de bataille

Chaque engagement possède désormais un `BattleTerrain`.

Pour un combat dans une province :

`BattleTerrain = Province.Terrain`

Pour une rencontre sur une liaison, le terrain dépend de la position exacte du contact entre les deux provinces.

### Calcul du point de rencontre

Le moteur utilise la progression précédente et actuelle des deux divisions afin de déterminer le moment précis où leurs trajectoires se croisent pendant le tick.

La position du contact est représentée par une valeur comprise entre :

0.0 = province A
1.0 = province B

Cette méthode permet de calculer correctement la rencontre même lorsque les deux divisions se déplacent à des vitesses différentes.

### Détermination du terrain sur une liaison

La liaison est actuellement divisée en deux zones abstraites :

0.00 à 0.50 :
terrain de la province A

0.50 à 1.00 :
terrain de la province B

Exemple :

A = Mountain
B = Plains

Contact à 60 % :

BattleTerrain = Plains

Cette abstraction permet d'utiliser les terrains déjà définis pour les provinces sans introduire une nouvelle carte de terrain propre aux liaisons.

### Modificateurs de terrain par type de division

Le terrain affecte désormais la pression de combat produite par chaque division.

Les différents types de formations ne réagissent pas de la même manière au terrain.

Les blindés sont notamment très efficaces en terrain ouvert mais fortement pénalisés dans les montagnes, marais, forêts et zones urbaines.

Exemple de prototype :

Armored + Plains :
BasePressure × 1.15

Armored + Mountain :
BasePressure × 0.45

Infantry + Mountain :
BasePressure × 0.90

Une division disposant d'une puissance de base supérieure peut donc devenir moins efficace qu'une formation plus légère lorsque le terrain lui est défavorable.

### Meeting Engagement

Dans un combat de rencontre :

- aucun camp n'est automatiquement considéré comme attaquant ou défenseur ;
- les deux camps combattent sur le même terrain ;
- chaque division reçoit néanmoins le modificateur correspondant à son propre type.

Le terrain reste donc déterminant sans introduire un avantage défensif artificiel.

### Province Battle

Les combats de province utilisent également les modificateurs de terrain propres aux différents types de divisions.

La distinction attaquant/défenseur et les bonus défensifs seront introduits dans une itération séparée.

### Tests ajoutés

- calcul d'un point de rencontre à 60 % d'une liaison ;
- sélection du terrain de la province correspondant au point de contact ;
- utilisation du terrain de la province lors d'un `ProvinceBattle` ;
- vérification de la forte pénalité des blindés en montagne ;
- vérification que le terrain modifie effectivement la pression organisationnelle produite pendant un combat.

# COMMIT b43fdc5a876f6d12f7934d8dc9350d06296d024e # 

## Itération — Synchronisation physique des Meeting Engagements

Le calcul précis du point de rencontre introduit précédemment est désormais également appliqué à la position réelle des divisions sur une liaison.

### Progression fractionnaire

`TransitState` peut maintenant représenter une progression continue sur une liaison.

`ElapsedHours` et `RemainingHours` utilisent des valeurs fractionnaires, ce qui permet de représenter des positions telles que :

- 55.5 %
- 57.5 %
- 63.27 %

au lieu d'être limité aux positions correspondant à des ticks entiers.

### Synchronisation au point de contact

Lorsqu'un `MeetingEngagement` est détecté, les deux divisions sont désormais repositionnées exactement au point de rencontre calculé.

Exemple :

France :
50 % -> 75 %

Allemagne :
60 % -> 40 %
dans le référentiel commun A -> B.

Point de contact calculé :

55.556 %

Après détection :

France A -> B :
Progress = 55.556 %

Allemagne B -> A :
Progress = 44.444 %

Les deux valeurs représentent exactement le même emplacement physique.

### Pause de l'engagement

Une fois leur progression synchronisée, les deux formations sont mises en pause par leur entrée dans l'engagement.

Elles ne conservent donc plus artificiellement la position atteinte à la fin du tick après avoir dépassé leur véritable point de rencontre.

### Retraite après un combat sur liaison

`TransitState.Reverse()` prend désormais correctement en charge les positions fractionnaires.

Une division vaincue peut faire demi-tour depuis le point exact du combat sans téléportation ou perte de précision.

### Limite temporelle actuelle

La simulation reste basée sur des ticks horaires.

La position du contact est calculée à l'intérieur du tick, mais la consommation de carburant du mouvement reste actuellement traitée à l'échelle du tick complet.

Une éventuelle résolution sous-horaire des événements sera étudiée ultérieurement.

### Maintenance du dépôt

Un fichier `.gitignore` a été ajouté afin d'exclure les artefacts de compilation .NET.

Les dossiers `bin/` et `obj/` précédemment suivis par Git ont été retirés de l'index tout en restant disponibles localement.

# COMMIT 3722d32f4f1740478a7e8f3247493d80e478d1e2 #
## Itération — Identification des attaquants et défenseurs

Les engagements ayant lieu dans une province peuvent désormais distinguer les forces attaquantes des forces défenseuses lorsque l'origine du combat est connue.

### EngagementRole

Chaque participant peut désormais posséder l'un des rôles suivants :

- `None`
- `Attacker`
- `Defender`

Les rôles sont stockés directement dans l'`Engagement`.

### Province Battle

Lorsqu'une division termine son mouvement dans une province contenant déjà une force ennemie stationnaire :

- le pays de la formation arrivante constitue le camp attaquant ;
- le pays de la formation déjà stationnée constitue le camp défenseur.

Les autres divisions appartenant à ces mêmes pays héritent du rôle correspondant.

Cette approche évite de considérer automatiquement toute division arrivée pendant le tick comme attaquante, notamment lorsqu'un renfort du défenseur arrive simultanément.

### Meeting Engagement

Les engagements ayant lieu sur une liaison restent symétriques.

Aucun participant à un `MeetingEngagement` n'est automatiquement considéré comme attaquant ou défenseur.

Leur rôle reste :

`EngagementRole.None`

### Provenance des rôles

`SimulationEngine` conservait déjà la liste des divisions ayant terminé un transit pendant le tick.

Cette information est désormais transmise à `EngagementSystem` afin de distinguer une formation venant d'entrer dans la province d'une formation déjà présente.

Le rôle n'est donc pas déduit arbitrairement du propriétaire ou du contrôleur de la province.

### Cas sans historique de mouvement

Lorsque plusieurs divisions ennemies sont directement présentes dans une même province sans qu'une arrivée puisse être identifiée, aucun rôle artificiel n'est attribué.

Cela permet notamment aux scénarios de tests construits directement dans une province de rester neutres.

### Renforts

Les renforts rejoignant une bataille de province héritent du rôle déjà associé à leur pays lorsque celui-ci est connu.

### Effets sur le combat

Cette itération introduit uniquement l'identification des rôles.

Aucun bonus ou malus de combat n'est encore associé à `Attacker` ou `Defender`.

Les effets défensifs, retranchements et autres modificateurs seront introduits séparément.

### Tests ajoutés

- une division arrivant dans une province ennemie est identifiée comme attaquante ;
- une division ennemie déjà stationnée est identifiée comme défenseuse ;
- un `MeetingEngagement` ne possède aucun attaquant ou défenseur ;
- un combat créé sans historique d'arrivée ne reçoit pas de rôles arbitraires.


# COMMIT 4c8d3a52bf4e4662d52dbec8097873c0165ea1b0 # 
## Itération — Première représentation physique de la composition des divisions

Le modèle des divisions commence à évoluer d'un système reposant principalement sur des types abstraits vers une représentation physique de leur composition.

### DivisionComposition

Chaque division possède désormais une `DivisionComposition`.

La première version représente quatre éléments :

- manpower ;
- infantry equipment ;
- artillery ;
- tanks.

Ces catégories constituent volontairement une première abstraction et seront enrichies ultérieurement.

### StrengthPool

Chaque composant est représenté par un `StrengthPool`.

Un pool distingue :

Authorized

la quantité théorique correspondant à l'effectif ou à la dotation normale de la division ;

Current

la quantité réellement disponible.

Il expose également :

Missing

et :

AvailabilityRatio

afin de représenter simplement le niveau de complétude d'une formation.

### Pertes

`ApplyLoss()` retire une quantité d'un pool sans jamais permettre de valeur négative.

La méthode retourne la perte réellement appliquée.

Une perte supérieure aux effectifs disponibles ramène donc simplement le pool à zéro.

### Renforts

`Reinforce()` permet de réintroduire du personnel ou du matériel.

La quantité présente ne peut jamais dépasser la dotation autorisée.

Cette mécanique constituera plus tard le point de connexion avec les systèmes de remplacement, de production et de logistique.

### Compatibilité avec DivisionType

`DivisionType` reste actuellement présent car les systèmes existants de mouvement, consommation de carburant et combat l'utilisent encore.

Un `PrototypeDivisionCompositionFactory` fournit temporairement une composition par défaut aux types :

Infantry
Motorized
Armored

afin de conserver la compatibilité avec les scénarios et tests existants.

Ces compositions provisoires ne constituent pas encore des données historiques ou définitives.

### Composition explicite

Une division peut désormais recevoir directement une composition spécifique lors de sa création.

Il devient donc possible de représenter des formations ayant une structure différente même lorsqu'elles partagent encore le même `DivisionType`.

### Effets sur le combat

La composition physique n'influence pas encore la résolution des combats.

Cette séparation est volontaire : cette itération introduit uniquement le modèle de données nécessaire au futur système de combat physique.

### Tests ajoutés

Les tests vérifient notamment :

- l'initialisation à l'effectif théorique ;
- l'impossibilité d'obtenir une quantité négative ;
- l'impossibilité de dépasser la dotation autorisée ;
- l'application correcte des pertes ;
- l'application correcte des renforts ;
- l'utilisation d'une composition explicite par une division ;
- la conservation de la compatibilité avec les types de divisions existants.


# COMMIT 089611517d8a4ae64949e57e7e85fb42936724c7 # 
## Itération — Puissance de combat dérivée de la composition physique

La puissance de base d'une division n'est désormais plus déterminée directement par son `DivisionType`.

Elle est dérivée de sa composition physique actuelle.

### CombatPowerProfile

La puissance brute d'une formation est séparée en plusieurs composantes :

- `SmallArms`
- `Artillery`
- `Armored`

La propriété `Total` représente pour l'instant la somme de ces composantes et peut être utilisée par le système de combat existant.

Cette séparation prépare des interactions futures spécifiques au terrain, à l'armure, à l'antichar et aux différentes catégories de munitions.

### Puissance des armes légères

La contribution `SmallArms` dépend du nombre d'équipements d'infanterie réellement utilisables.

Le nombre utilisable est actuellement limité par le personnel disponible.

Une formation possédant davantage de fusils que d'hommes ne bénéficie donc pas de la puissance des armes sans opérateurs disponibles.

### Artillerie et chars

L'artillerie et les chars produisent une puissance propre en fonction de leur quantité présente dans la division.

Leur efficacité est également provisoirement modulée par la disponibilité globale du personnel afin de représenter la nécessité de disposer d'équipages.

### Disparition du BaseCombatPressure par type

Les anciennes valeurs :

Infantry = 6
Motorized = 7.5
Armored = 10

ne déterminent plus directement la puissance de base.

Une division blindée est désormais puissante parce qu'elle possède réellement des chars, de l'artillerie et du personnel.

### Pertes matérielles

Les valeurs `Current` des `StrengthPool` influencent immédiatement la puissance calculée.

Une division passant de :

180 tanks

à :

90 tanks

voit donc naturellement diminuer sa composante blindée sans changer de `DivisionType`.

### Pertes humaines

La réduction du manpower diminue également la capacité à employer les armes et équipements présents.

Cette représentation des équipages reste provisoire et sera raffinée lorsque le modèle distinguera davantage les différentes fonctions du personnel.

### Intégration au combat

Le système de combat utilise désormais :

Composition
→ Raw Combat Power
→ Fuel Effectiveness
→ Terrain Modifier
→ Effective Combat Pressure

Les pertes physiques ne sont pas encore générées automatiquement par les combats.

La composition influence donc déjà le résultat d'une bataille, mais elle n'est pas encore modifiée par celle-ci.

### Compatibilité temporaire

`DivisionType` reste utilisé par plusieurs systèmes existants :

- mouvement ;
- consommation de carburant ;
- modificateurs globaux de terrain.

Ces dépendances seront progressivement remplacées par des propriétés dérivées de la composition.

### Limites actuelles

- les coefficients de puissance sont provisoires ;
- le terrain affecte encore toute la division via `DivisionType` ;
- les munitions ne modifient pas encore la puissance ;
- aucune distinction entre modèles individuels de chars, canons ou armes n'existe ;
- les pertes physiques de combat ne sont pas encore calculées ;
- le personnel spécialisé et les équipages ne sont pas encore représentés.

# COMMIT be06ab7157d4c772adefc0077e0cb6d7952bab4c # 
## Itération — Modificateurs de terrain par composante de combat

Le terrain n'est désormais plus appliqué globalement à une division selon son `DivisionType`.

Il affecte séparément les différentes composantes de sa puissance de combat.

### TerrainCombatModifiers

Chaque terrain définit actuellement trois coefficients :

- SmallArms
- Artillery
- Armored

Exemple pour la montagne :

SmallArms = 0.90
Artillery = 0.75
Armored = 0.45

Les valeurs restent provisoires et servent actuellement à établir le comportement du modèle.

### Application composante par composante

Le `CombatPowerProfile` brut issu de la composition est transformé par le terrain avant le calcul de la pression finale.

Exemple :

SmallArms = 2.50
Artillery = 1.08
Armored = 6.30

en montagne devient approximativement :

SmallArms = 2.25
Artillery = 0.81
Armored = 2.84

La puissance totale effective résulte ensuite de la somme de ces composantes.

### Disparition du terrain basé sur DivisionType

`DivisionType` ne détermine plus directement l'effet du terrain sur la puissance de combat.

Deux divisions possédant le même `DivisionType` mais des compositions différentes peuvent désormais réagir différemment au même terrain.

### Formations mixtes

Une formation contenant à la fois de l'infanterie, de l'artillerie et des chars ne reçoit plus un malus global de formation blindée.

Chaque composante conserve son comportement propre.

Cela permet aux formations mixtes de produire naturellement des résultats différents sans nécessiter de nouveaux types de divisions.

### Meeting Engagement et Province Battle

Les deux types d'engagement continuent d'utiliser le `BattleTerrain` déjà déterminé par le moteur.

La différence réside uniquement dans la manière dont ce terrain agit sur les composantes physiques de chaque formation.

### Compatibilité

`DivisionType` reste actuellement utilisé par :

- le mouvement ;
- la consommation de carburant ;
- l'efficacité liée au carburant.

Son rôle dans le calcul direct du terrain de combat est supprimé.

### Limites actuelles

- les coefficients de terrain restent provisoires ;
- le carburant affecte encore globalement toute la puissance de la division ;
- les munitions ne sont pas encore différenciées par catégorie ;
- SmallArms, Artillery et Armored restent des catégories agrégées ;
- l'effet du rôle Attacker/Defender n'est pas encore appliqué à la résolution du combat.


# COMMIT 58a9c4a9456e2c1193a8568fb9ab8cd00db7432f #
## Itération — Consommation et effets du carburant dérivés de la composition

Le carburant utilisé pendant un combat n'est désormais plus déterminé directement par `DivisionType`.

La consommation est dérivée des équipements réellement présents dans la division.

### Trucks

La composition physique représente désormais également les camions.

Les camions ne produisent pas directement de puissance de feu, mais contribuent à la consommation de carburant et prépareront les futurs systèmes de mobilité et de logistique.

### Demande de carburant

Le moteur calcule une demande de carburant de combat à partir des quantités actuellement disponibles de :

- trucks ;
- artillery ;
- tanks.

Chaque catégorie possède une consommation provisoire propre.

La perte de véhicules ou de chars réduit donc naturellement la consommation future de la formation.

### Personnel disponible

La demande de carburant est modulée par la disponibilité du personnel.

Du matériel sans équipage ne produit ni puissance de combat normale ni consommation opérationnelle complète.

### Fuel Satisfaction

La quantité réellement consommée est comparée à la demande de carburant.

Le ratio obtenu est appelé `fuelSatisfaction`.

Une satisfaction de :

1.0

signifie que toute la demande a été fournie.

Une satisfaction de :

0.0

signifie qu'aucun carburant demandé n'a pu être fourni.

### Effets composante par composante

Le manque de carburant n'affecte plus toute la division uniformément.

Les effets actuels à zéro carburant sont approximativement :

SmallArms = 95 % de l'efficacité normale
Artillery = 70 %
Armored = 20 %

Les chars sont donc beaucoup plus sensibles au manque de carburant que l'infanterie à pied.

### Suppression de la dépendance au DivisionType

Les anciennes méthodes déterminant :

- le carburant de combat par type ;
- l'efficacité minimale par type ;

ont été supprimées.

La consommation et l'effet du carburant sont désormais dérivés de la composition physique et des composantes de puissance.

### Chaîne de calcul du combat

La pression de combat suit désormais :

Composition
→ CombatPowerProfile brut
→ modificateurs de terrain par composante
→ calcul de la demande de carburant
→ satisfaction de la demande
→ modificateurs de carburant par composante
→ pression de combat totale

### Compatibilité temporaire

`DivisionType` reste encore utilisé pour le mouvement et sa consommation de carburant.

La consommation de déplacement sera migrée ultérieurement vers un modèle dérivé de la composition.

### Limites actuelles

- toutes les catégories utilisent encore un réservoir commun ;
- aucune priorité de distribution du carburant entre équipements n'existe ;
- les camions sont encore représentés comme une catégorie agrégée ;
- la consommation de déplacement dépend encore de `DivisionType` ;
- les valeurs de consommation et les coefficients d'efficacité restent provisoires.


# COMMIT 9db0f034ad61f964f34afb66a88283ad737e02aa  #
## Itération — Pertes humaines physiques au combat

Le combat commence désormais à produire des pertes physiques persistantes en plus des pertes d'organisation.

### PersonnelPool

Le manpower d'une division n'est plus représenté par un simple `StrengthPool`.

Le nouveau `PersonnelPool` distingue :

- personnel actuellement apte au combat ;
- tués au combat ;
- blessés actuellement indisponibles ;
- nombre cumulé de blessés ;
- disparus ou prisonniers.

### KIA et WIA

Les pertes causées directement par les combats sont actuellement réparties entre :

Killed In Action
Wounded In Action

La proportion initiale est calibrée autour des ordres de grandeur historiques de la Seconde Guerre mondiale.

Les disparus et prisonniers ne sont pas générés par le feu normal : ils seront liés ultérieurement aux retraites, encerclements et redditions.

### Sources des pertes

La capacité à produire des pertes humaines dépend séparément de :

SmallArms
Artillery
Armored

L'artillerie reçoit actuellement une pondération supérieure afin de refléter son importance historique dans la production des pertes sur les champs de bataille de la Seconde Guerre mondiale.

### Calibration générale

Le coefficient de pertes initial est choisi afin qu'un affrontement soutenu entre formations comparables produise un ordre de grandeur proche des taux historiques observés pour une division en ligne.

Les combats beaucoup plus intenses devront pouvoir atteindre les taux constatés lors des journées de combat sévère.

### Application simultanée

Toutes les pertes d'une heure sont calculées avant leur application.

Les deux camps combattent donc avec l'état qu'ils possédaient au début du tick.

### Blessés

Les blessés quittent immédiatement le manpower disponible mais ne sont pas considérés comme des pertes permanentes.

Le système permet déjà leur retour au service via `ReturnWoundedToDuty()`.

La durée de récupération et le risque de mort des blessures ne sont pas encore simulés.

### Limites actuelles

- les coefficients restent une première calibration ;
- le hasard statistique n'est pas encore introduit ;
- les blessures ne possèdent pas encore de gravité ;
- la qualité du service médical n'est pas encore prise en compte ;
- les MIA/POW seront calculés lors des retraites et redditions ;
- les pertes matérielles seront traitées séparément afin de distinguer matériel endommagé, détruit, abandonné et capturé.


# COMMIT 3aab73e386013d486391fd9ef0821e68c303196b # 
## Itération — Pertes blindées et équipages

Le système de pertes physiques représente désormais les premiers dommages matériels au combat.

Cette première version se concentre volontairement sur les chars.

### TankPool

Les chars ne sont plus représentés par un simple `StrengthPool`.

Le nouveau `TankPool` distingue :

- `Operational`
- `Damaged`
- `Destroyed`

`Destroyed` représente un cumul historique tandis que `Operational` et `Damaged` décrivent les véhicules actuellement détenus par la division.

### Chars endommagés

Un char endommagé reste physiquement présent dans la formation mais cesse de contribuer :

- à la puissance blindée ;
- à la consommation normale de carburant de combat.

Une future mécanique permettra leur réparation.

### Chars détruits

Un char détruit quitte définitivement l'inventaire actuel de la division.

Les remplacements futurs pourront restaurer la dotation opérationnelle sans effacer le compteur historique des véhicules détruits.

### Pertes fractionnaires

Les pertes matérielles attendues peuvent être inférieures à une unité par heure.

Le moteur conserve donc des accumulateurs fractionnaires.

Exemple :

0.20 char détruit attendu par heure

Après plusieurs ticks, l'accumulation produit une destruction physique réelle.

Cette méthode permet de conserver un moteur déterministe sans supprimer les événements rares par arrondi.

### Sources des pertes blindées

La première version considère :

- `Armored` comme principale source de pertes antichars ;
- `Artillery` comme source secondaire de dégâts et destructions ;
- `SmallArms` comme incapable de détruire directement les chars.

Une future composante `AntiTank` affinera fortement ce modèle.

### Équipages

Les chars touchés peuvent désormais produire des pertes humaines parmi leurs équipages.

Un char détruit présente un risque beaucoup plus important pour son équipage qu'un char seulement endommagé.

Une perte de char ne signifie cependant jamais automatiquement la mort de tout son équipage.

### Prévention du double comptage

Les équipages affectés aux chars sont retirés du manpower soumis au calcul général des pertes humaines.

Les pertes d'équipage sont ensuite calculées séparément à partir des dommages réels subis par les chars.

### Limites actuelles

- les équipages restent stockés dans le `PersonnelPool` global ;
- la réaffectation de nouveaux équipages est encore implicite ;
- aucune distinction entre modèles de chars n'existe ;
- la taille d'équipage est provisoirement fixée à cinq personnes ;
- les chars endommagés ne sont pas encore réparés automatiquement ;
- les véhicules abandonnés ou capturés ne sont pas encore représentés ;
- les canons, camions et équipements d'infanterie utilisent encore `StrengthPool`.

# COMMIT f383b3d304d08b29e0c0c6fe663774e32847c69d #
## Itération — Premier client graphique Avalonia

Un premier client graphique desktop a été ajouté au prototype WoF.

### WoF.Client

Un nouveau projet `WoF.Client` utilisant Avalonia affiche désormais l'état du moteur de simulation.

Le client référence directement `WoF.Simulation` et n'implémente aucune logique militaire propre.

### Carte prototype

Un scénario fictif composé de neuf provinces permet de visualiser :

- les provinces ;
- leurs connexions ;
- leur terrain ;
- leur contrôleur ;
- les divisions présentes.

Les coordonnées d'affichage sont conservées dans le client et ne font pas partie du modèle `Province`.

### Divisions

Les divisions françaises et allemandes sont représentées directement sur la carte.

Lorsqu'une division est en transit, sa position graphique est interpolée entre la province d'origine et la province de destination à partir de `Transit.Progress`.

La position visuelle correspond donc à la position calculée par le moteur.

### Commandement

Le joueur peut :

- sélectionner une division française ;
- sélectionner une province ;
- donner un ordre de mouvement ;
- faire avancer la simulation d'une heure ;
- réinitialiser le scénario.

Les ordres utilisent directement `SimulationEngine.TryOrderMoveTo()`.

### Inspection

Le panneau latéral affiche notamment :

- état opérationnel ;
- position ;
- organisation ;
- carburant ;
- manpower disponible ;
- KIA cumulés ;
- WIA indisponibles.

### Objectif

Cette interface n'a pas encore vocation à représenter l'apparence finale du jeu.

Elle constitue un visualisateur interactif permettant de tester et observer directement les mécaniques du moteur de simulation.

### Limites actuelles

- carte en graphe simplifiée ;
- aucune génération procédurale ;
- aucune caméra ou zoom ;
- pas de multisélection ;
- pas de véritable système graphique de pions militaires ;
- pas encore d'affichage spécifique des engagements ;
- le client lit encore directement les objets publics du moteur.


# COMMIT 56bbc54b6f2ca16db95ef09cede22bf0001781ff #
## Itération — Visualisation des engagements, dépôts et commandement des deux camps

Le client graphique Avalonia permet désormais d'observer plus clairement le déroulement des combats et de commander l'un ou l'autre camp.

### Commandement des deux camps

L'interface permet maintenant de choisir le camp commandé.

Le sélecteur de divisions se met à jour en fonction du pays sélectionné, et il est également possible de sélectionner directement une division sur la carte, ce qui bascule automatiquement le commandement sur son camp.

### Dépôts

Le scénario prototype contient désormais des dépôts de ravitaillement visibles sur la carte.

Chaque dépôt possède :

- une province ;
- un stock de carburant ;
- un débit de transfert horaire.

Ces dépôts sont ajoutés au moteur de simulation et peuvent ravitailler les divisions stationnaires connectées par le réseau logistique.

### Marqueurs de bataille

Les engagements sont maintenant visibles directement sur la carte :

- une bataille en province est affichée par un marqueur au-dessus de la province ;
- un engagement sur liaison est affiché par un marqueur positionné au point moyen des unités engagées sur cette liaison.

### Informations enrichies

L'inspecteur latéral affiche toujours l'état général d'une unité, avec en plus un meilleur suivi du matériel blindé :

- chars opérationnels ;
- chars endommagés ;
- chars détruits.

L'inspection d'une province indique également si un dépôt s'y trouve.

### Objectif

Cette itération renforce la valeur du prototype comme outil d'observation du moteur :

- visualiser les combats ;
- tester le ravitaillement ;
- observer le comportement des deux camps ;
- déboguer plus facilement les situations de mouvement et d'engagement.

## Correction — Détection des rencontres lors d'une arrivée

Un cas limite permettait précédemment à deux divisions ennemies de se traverser sur une liaison.

Le problème apparaissait lorsqu'une division terminait son mouvement vers une province pendant le même tick qu'une division ennemie quittait cette province dans la direction opposée.

### Cause

Une division atteignant 100 % de progression supprimait immédiatement son `TransitState`.

La détection des `MeetingEngagements` intervenant ensuite, le moteur ne possédait plus la trajectoire de cette division et ne pouvait plus détecter le croisement.

### Nouvelle séquence

La finalisation des arrivées est désormais séparée du déplacement.

Le tick suit maintenant l'ordre :

1. avance des mouvements ;
2. détection des engagements sur liaison ;
3. finalisation des arrivées restantes ;
4. détection des engagements en province.

Une division peut donc temporairement posséder un transit terminé (`Progress = 1.0`) jusqu'à la fin de la phase de détection des collisions.

### Résultat

Une division proche de sa destination ne peut plus traverser une unité ennemie quittant cette destination pendant le même tick.

Le point exact de rencontre déjà calculé par le moteur est utilisé pour synchroniser les deux formations avant la création du `MeetingEngagement`.


# COMMIT dfecee58e228b3eb220d27f144a5dc4bed786a2d # 

Un bug sur l'interface graphique avalonia faisait que l'UI de l'engagement divisions était tout en noir, rendant les valeurs invisibles, 
un premier commit révélant certaines valeurs (terrain, pourcentage de l'avancée entre les deux terrains (interengagements))

Egalement, en testant l'interface Avalonia, cela a révélé un bug coté code, lorsqu'un engagement avait lieu en 
interprovince et qu'une division tierce croisait le chemin de cet engagement, elle ne s'engageait pas dans le combat en question, cela a été géré par 
- AddReinforcementsToTransitEngagements() 

Qui gère ce nouveau cas précisément 


# COMMIT 2c5da9200ec1faad6abd78b065c10a3101efd62a #
## Itération — Battle Ledger : historique des batailles

Cette itération introduit les premières fondations d'un registre historique des engagements militaires.

### Captures initiales

Lorsqu'une division rejoint une bataille, le moteur enregistre un `ParticipantBattleSnapshot` contenant une copie de ses statistiques à cet instant.

Les valeurs suivies incluent :
- manpower disponible ;
- KIA et WIA cumulés ;
- disparus ou prisonniers ;
- chars opérationnels, endommagés et détruits ;
- organisation ;
- carburant.

### Rapports individuels

À la fin d'un engagement, un `ParticipantBattleReport` est produit pour chaque division participante.

Il permet de connaître les pertes propres à cette bataille en comparant les compteurs cumulés au début et à la fin de la participation.

### Renforts

Les divisions rejoignant un engagement déjà commencé possèdent leur propre heure d'arrivée et leur propre état initial.

Les pertes antérieures à leur entrée ne sont pas attribuées à cette bataille.

### BattleReport

Chaque bataille terminée produit un rapport contenant :
- son identifiant ;
- son type ;
- son lieu et son terrain ;
- ses heures de début et de fin ;
- son issue ;
- la liste des participants et leurs pertes.

### BattleHistory

Les rapports sont archivés dans `BattleHistory`.

Ils restent consultables indépendamment de l'évolution ultérieure des divisions.

Accès depuis le moteur : `SimulationEngine.Engagements.History.Reports`.

L'heure courante est transmise explicitement à l'ajout de chaque participant et
au traitement des engagements. Un nouvel appel pour un participant déjà présent
ne remplace ni son snapshot initial ni son heure d'entrée.

Les snapshots finaux sont capturés après l'application des conséquences du
combat, avant la retraite, le ravitaillement et la récupération d'organisation.
Les rapports ne conservent aucune référence aux divisions ou à leurs pools :
les identités sont copiées sous forme de noms et d'identifiants, et les
statistiques sous forme de valeurs. La collection des participants est copiée
et exposée en lecture seule. Chaque engagement ne peut être archivé qu'une fois.

### Compteurs et variations

- `Killed` : différence de `KilledInAction` ;
- `Wounded` : différence de `TotalWoundedInAction`, indépendante du retour des blessés ;
- `MissingOrCaptured` : différence du cumul des disparus/prisonniers ;
- `TanksDestroyed` : différence du cumul des chars détruits ;
- `DamagedTanksChange` : variation nette du stock endommagé, qui peut être négative après réparation.

La variation du manpower, de l'organisation ou des stocks ne constitue pas un
compteur brut de pertes : les soins, renforts et réparations peuvent modifier
ces valeurs indépendamment des pertes subies.

### Tests du Battle Ledger

- copie des statistiques sans référence aux pools vivants ;
- exclusion des pertes antérieures et utilisation des WIA cumulés après soins ;
- snapshots et heures propres aux renforts en province et sur liaison, dans les deux sens ;
- interception d'un renfort avant son arrivée et sa capture territoriale ;
- troisième participant rejoignant une rencontre pendant son tick de création ;
- conservation du snapshot lors d'un ajout répété ;
- archivage avec le vainqueur, ou sans vainqueur, et les heures du moteur ;
- capture avant la récupération post-combat ;
- indépendance des engagements successifs et immuabilité des rapports ;
- copie défensive des collections et refus des doubles archivages ;
- distinction entre stock endommagé et dommages bruts.

### Limites

- historique conservé uniquement en mémoire ;
- aucune sauvegarde sur disque ;
- aucun écran de consultation dans Avalonia ;
- pas encore de journal détaillé de chaque heure ;
- distinction des dommages matériels bruts et des variations de stock à développer ;
- l'attribution précise des pertes infligées à une division ennemie n'est pas encore disponible.

# COMMIT e7a472ad2297a80040d204f4d045fd2462ed8af3 # 

## Itération A — Fondations des nouvelles fondations d'architecture des troupes 

### Objectif

Introduire une organisation militaire hiérarchique
Division → Battalion → Company, en préparation du
futur moteur de combat tactique.

### Fonctionnalités

- Création des classes Battalion et Company.
- Classification des formations par BattalionType et CompanyType.
- Rattachement de bataillons aux divisions.
- Rattachement de compagnies aux bataillons.
- Navigation dans la hiérarchie militaire.
- Protection contre les rattachements multiples.
- Vérification de l'unicité des identifiants au sein d'un parent.
- Collections accessibles en lecture seule.
- Tests de construction d'une hiérarchie de grande taille.

### Architecture

Division conserve les ordres stratégiques.

Battalion constituera l'unité tactique principale.

Company représentera les composantes du bataillon
et recevra ultérieurement ses ressources physiques.

### Compatibilité

Le système de combat actuel reste inchangé.

DivisionComposition demeure temporairement la source
de vérité des ressources, jusqu'à la migration
prévue dans l'itération B.

### Limites

- Aucun effectif attribué aux compagnies.
- Aucun équipement attribué aux compagnies.
- Pas encore de transfert de formations.
- Pas de rattachement opérationnel temporaire.
- Pas de déploiement tactique.
- Pas de combat au niveau du bataillon.
- Aucun benchmark de performance représentatif
  du futur moteur de combat.

# COMMIT 3e69e04e0daddca2ca2d3f12043165762585ed7a #
## Itération B1 — Hierarchical Resource Accounting

### Objectif
Introduire les ressources physiques des compagnies et
la comptabilité hiérarchique des formations militaires.

### Fonctionnalités
- Création de CompanyComposition.
- Réutilisation des PersonnelPool, StrengthPool et TankPool.
- Attribution de ressources propres aux compagnies.
- Introduction de FormationResourceTotals.
- Agrégation des ressources des compagnies vers les bataillons.
- Agrégation des ressources des bataillons vers les divisions.
- Captures de totaux immuables.
- Protection contre le partage d'une composition physique.
- Détection des compagnies sans composition renseignée.
- Tests des pertes et de leur répercussion dans les totaux.

### Limites
- Comptabilité hiérarchique expérimentale.
- DivisionComposition reste utilisée par le combat actuel.
- Pas encore de migration des pertes de combat au niveau compagnie.
- Aucun modèle de personnel par fonction militaire.
- Aucun inventaire détaillé des armes.
- Aucun benchmark de performance tactique.

### Prochaine étape
B2 : préparer la migration contrôlée des ressources
divisionnaires vers la hiérarchie sans duplication.

# COMMIT cc141c43834b9fc681abf56b31fd40e6fa98736c #
## Itération B2.1 — Unified Resource Reading

### Objectif

Découpler les calculs militaires de l'emplacement
physique des ressources.

### Modifications

- FormationResourceTotals accepte DivisionComposition
  et CompanyComposition.
- Mutualisation de la capture des ressources.
- Ajout de Division.GetCurrentResourceTotals().
- Adaptation du calcul de puissance militaire.
- Adaptation du calcul de demande en carburant.
- EngagementSystem utilise les ressources agrégées
  pour les calculs de puissance et de carburant.
- Conservation des coefficients existants.
- Tests de compatibilité et de non-régression.

### Limites

- DivisionComposition reste la source physique active.
- Les pertes de combat ne sont pas encore réparties
  entre les compagnies.
- Le Battle Ledger et l'UI utilisent encore les
  accès existants qui seront migrés ensuite.
- Pas encore d'activation du modèle hiérarchique.

### Prochaine étape

B2.2 — Application des pertes et des modifications
de ressources aux compagnies, sans duplication.

# COMMIT 13a3a416e13f4e17edebe535d06c61b1129ef036 #

## Itération B2.2 — Hierarchical Personnel Loss Distribution

### Objectif

Permettre la répartition des pertes humaines calculées
pour une division entre ses compagnies.

### Fonctionnalités

- Ajout de HierarchicalPersonnelLossApplier.
- Répartition proportionnelle des pertes humaines.
- Attribution séparée des KIA, WIA et disparus/capturés.
- Gestion déterministe des arrondis.
- Conservation exacte des pertes entières.
- Validation globale avant toute mutation physique.
- Rejet des pertes impossibles et des formations incomplètes.
- Retour des allocations par bataillon et compagnie.
- Tests de répartition, de cohérence et de non-régression.

### Architecture

Les pertes sont appliquées aux PersonnelPool des compagnies.

Les totaux des bataillons et divisions sont recalculés
par agrégation des ressources de leurs subordonnés.

### Limites

- Le service n'est pas encore connecté au combat actuel.
- DivisionComposition reste la source active dans Avalonia.
- La répartition proportionnelle est provisoire.
- L'exposition tactique des compagnies n'est pas encore simulée.
- Les pertes de chars et de matériel ne sont pas encore migrées.

### Suite prévue

B2.2b : répartition du matériel et des pertes de chars,
avec conservation des accumulations fractionnaires.

B2.3 : activation contrôlée de la source de ressources
hiérarchique dans le combat, l'interface et le Battle Ledger.


# COMMIT 7027cb69722585c358c712dfa26cb4fd413e7d52 # 

## Itération B2.2b — Hierarchical Tank Loss Accounting

### Objectif
Permettre la répartition des pertes de chars entre
les compagnies sans perdre la comptabilité fractionnaire.

### Fonctionnalités
- Ajout de TankPool.ApplyResolvedLosses().
- Ajout de HierarchicalTankLossApplier.
- Accumulation fractionnaire par division.
- Conversion en pertes matérielles entières.
- Répartition déterministe entre les compagnies.
- Distinction chars opérationnels, endommagés et détruits.
- Validation préalable des stocks et compositions.
- Conservation des anciens systèmes de combat.
- Tests d'accumulation et de conservation des ressources.

### Limites
- Service non connecté à EngagementSystem.
- Politique de répartition provisoire.
- Pas encore de sauvegarde de l'accumulateur fractionnaire.
- Pas encore de pertes localisées selon l'exposition tactique.
- Pas encore de répartition des autres équipements.

### Prochaine étape
B2.2c : pertes et renforts des équipements ordinaires.


# COMMIT ee215f3694d496449419b3d57b9b06723e8ccd8f # 

## Itération B2.2c — Hierarchical Equipment Loss Accounting

### Objectif
Introduire la comptabilité des pertes d'équipements
ordinaires dans la hiérarchie militaire.

### Fonctionnalités
- Ajout de EquipmentLossReport.
- Ajout de HierarchicalEquipmentLossApplier.
- Répartition des pertes d'armes d'infanterie.
- Répartition des pertes d'artillerie.
- Répartition des pertes de camions.
- Répartition proportionnelle et déterministe.
- Validation de toutes les demandes avant mutation.
- Conservation exacte des quantités entières.
- Retour des allocations par compagnie.
- Tests de cohérence et de non-régression.

### Limites
- Le service n'est pas connecté au combat actuel.
- La répartition reste provisoirement proportionnelle.
- Les renforts ne sont pas encore liés à une source physique.
- Les stocks divisionnaires historiques restent actifs.
- Pas encore de simulation tactique du matériel.

### Suite
B2.2d : gestion des renforts et transferts de ressources.

B2.3 : raccordement de la comptabilité hiérarchique
au moteur de combat, au Battle Ledger et à Avalonia.

# COMMIT b46c4e4319d51b523afccc2db76fbb960948b04b # 

## Itération B2.2d — Reinforcement Resource Transfers

### Objectif

Introduire des renforts militaires provenant d'une réserve
identifiée, sans création ni duplication de ressources.

### Architecture

Ajout du module Military/Reinforcements.

- ReinforcementCargo : quantités transférables.
- ReinforcementReserve : stock physique de renforts.
- ReinforcementTransferService : validation et transfert.
- ReinforcementTransferReceipt : bilan du transfert.

### Fonctionnalités

- Transfert de personnel vers une compagnie.
- Transfert d'armes d'infanterie.
- Transfert de pièces d'artillerie.
- Transfert de chars opérationnels.
- Transfert de camions.
- Débit du stock de la réserve.
- Contrôle de la capacité d'accueil de la compagnie.
- Rejet des ressources insuffisantes.
- Validation complète avant mutation des stocks.
- Conservation des quantités transférées.
- Tests des transferts et des demandes impossibles.

### Principes

Les ressources des compagnies restent la source de vérité
du modèle hiérarchique.

Un renfort reçu par une compagnie augmente automatiquement
les totaux calculés de son bataillon et de sa division.

Les compteurs historiques de pertes ne sont pas effacés
lors d'un renforcement.

### Limites

- Transferts comptables immédiats uniquement.
- Réserves non géolocalisées.
- Pas de convois ni de délais de transport.
- Pas encore de transferts directs entre compagnies.
- Pas de production ni de recrutement de nouveaux stocks.
- Aucun raccordement au système de combat actuel.
- Pas de sauvegarde ou persistance des réserves.
- Le moteur reste séquentiel pour les mutations de stocks.

### Prochaine étape

B2.3 — Activation contrôlée des ressources hiérarchiques
dans le moteur de combat, le Battle Ledger et l'interface.

Un seul modèle de stockage physique devra être actif
pour chaque division.

# COMMIT 2f7138aad35f171ee61f2dd3eb6c7a2a122ebde5 # 

## Itération B2.3 — Hierarchical Resources Integration

### Objectif
Activer la comptabilité hiérarchique dans le moteur
de combat tout en préservant la compatibilité Legacy.

### Fonctionnalités
- DivisionResourceMode : Legacy et Hierarchical.
- Activation explicite des ressources hiérarchiques.
- Vérification de l'égalité des stocks à l'activation.
- Lecture unifiée des ressources actives.
- Application des pertes humaines à la source active.
- Application des pertes de chars à la source active.
- Conservation de l'accumulateur fractionnaire des chars.
- Adaptation de EngagementSystem.
- Adaptation de ParticipantBattleSnapshot.
- Adaptation de l'affichage des ressources dans Avalonia.
- Tests de combat entre divisions Legacy et Hierarchical.
- Tests de non-modification des stocks inactifs.

### Architecture
Division centralise l'accès aux ressources actives.

En mode Legacy :
- DivisionComposition conserve les ressources physiques.

En mode Hierarchical :
- CompanyComposition détient les ressources physiques.
- Battalion agrège les ressources des compagnies.
- Division agrège les ressources des bataillons.

### Limites
- L'ancienne représentation demeure en mémoire.
- Activation réservée à la préparation des scénarios.
- La répartition des pertes reste proportionnelle.
- Les pertes d'équipage ne ciblent pas encore
  spécifiquement les compagnies blindées.
- Le système de combat reste divisionnaire.
- Les sauvegardes des accumulateurs ne sont pas prévues.
- La composition historique détaillée reste à développer.
- Les cas extrêmes de pertes nécessitent davantage de tests.

### Suite
Itération C : benchmarks à grande échelle.

Mesurer les performances des hiérarchies militaires
et identifier les coûts d'agrégation et de combat.

# COMMIT 86aa9cbdc263c1e31cb755ca0230680be57d752f # 

## Itération C1 — Large Scale Simulation Benchmark

### Objectif

Mesurer les performances du moteur WoF avec plusieurs
centaines ou milliers de divisions et bataillons.

### Architecture

Nouveau projet autonome : WoF.Benchmarks.

Il référence WoF.Simulation sans dépendance Avalonia.

### Scénarios

- Stationary : formations au repos.
- Combat : engagements simultanés par paires
  dans des provinces distinctes.

### Organisation générée

- 12 bataillons par division.
- 4 compagnies par bataillon.
- 10 000 hommes théoriques par division.
- Comptabilité hiérarchique activée.
- Ressources réparties exactement entre les compagnies.

### Métriques

- Temps de construction.
- Allocations de construction.
- Empreinte mémoire managée estimée.
- Temps d'agrégation des ressources.
- Temps moyen par tick.
- Allocations par tick.
- Collections du GC.
- Nombre d'engagements actifs.

### Limites

- Organisation militaire fictive et régulière.
- Pas encore de mouvement massif.
- Pas de simulation de logistique complexe.
- Pas de carte tactique hybride.
- Mesures dépendantes du matériel et du runtime.
- Aucune garantie de performances en simulation V2.

### Prochaine étape

C2 : benchmark des mouvements simultanés,
des rencontres sur liaisons et du ravitaillement.

Puis optimisation des goulots d'étranglement mesurés,
avant l'introduction de la géométrie tactique hybride.

# COMMIT 5c574f35cf61762c165cc2f28347d3d55b55b951 #  

## Itération C1.1 — Concentrated Combat Benchmarks

### Objectif
Étudier les effets de la concentration des forces
sur les performances du moteur opérationnel WoF.

### Fonctionnalités
- Taille des engagements configurable.
- Nombre total de divisions constant.
- Répartition équilibrée entre pays adverses.
- Échauffement avant les mesures.
- Mesure individuelle des ticks.
- Calcul de moyenne, médiane, p95 et p99.
- Mesure des allocations mémoire par tick.
- Suivi du nombre de combats actifs.
- Export des résultats au format CSV.

### Expériences
- 500 divisions, 250 batailles de 2 divisions.
- 500 divisions, 50 batailles de 10 divisions.
- 500 divisions, 10 batailles de 50 divisions.
- 500 divisions, 1 bataille de 500 divisions.

### Limites
- Combats divisionnaires uniquement.
- Pas de déplacement ni de logistique dans ces scénarios.
- Distribution synthétique des forces.
- Pas de simulation tactique indépendante des bataillons.
- Les états militaires évoluent pendant les mesures.
- Les performances dépendent du matériel et du runtime.
- Les percentiles courts doivent être interprétés prudemment.

### Suite
Identifier les coûts dominants du moteur à partir des
mesures et d'un profilage CPU/mémoire.

Ne procéder qu'à des optimisations ciblées et
vérifiées par des benchmarks avant/après.

# COMMIT b63ec2a449354437241b4f861a9573476c87e4cb # 

## Itération C1.2a — Benchmark Instrumentation and Profiling

### Objectif
Fiabiliser les benchmarks de combat et identifier
les véritables sources de surcoût CPU et mémoire.

### Fonctionnalités
- Nouveau BenchmarkMilitaryState.
- Photographies militaires avant/après échauffement
  et après simulation mesurée.
- Suivi des participants capables de combattre.
- Suivi des divisions rompues.
- Comptabilité des effectifs, pertes et carburant.
- Nombre d'engagements actifs et archivés.
- Collections GC par tick.
- Export CSV enrichi.
- Export JSON avec configuration et révision Git.
- Marqueurs EventSource pour les ticks mesurés.
- Profilage externe avec dotnet-trace.

### Méthodologie
- Mesures en Release.
- Répartition des forces inchangée.
- Pas de modification des règles de combat.
- Diagnostics militaires complets hors chronométrage.
- Exécutions sans profileur pour les références.
- Traces collectées séparément pour identifier
  les fonctions coûteuses.
- Comparaison des résultats militaires et techniques.

### Limites
- Le GC peut intervenir entre deux ticks.
- Les allocations restent mesurées sur le thread courant.
- Les profils d'échantillonnage sont approximatifs.
- Les scénarios sont synthétiques et évolutifs.
- Les mesures courtes ne caractérisent pas les
  performances d'une campagne longue.

### Suite
C1.2b : optimiser les agrégations de combat par pays
dans EngagementSystem, après identification des coûts.

Conserver les règles d'hostilité et l'application
simultanée des pertes.

# COMMIT 10ad5f5523f3a648cd88d41845abb89598c719bc #

- Remplace MeasuredTickEnd par MeasuredTickStop.
- Déclare explicitement les opcodes Start/Stop et les tâches associées.
- Ajoute MeasurementStart/MeasurementStop pour délimiter les 72 ticks mesurés.
- Place les marqueurs hors des intervalles de mesure du temps et des allocations.
- Conserve les diagnostics militaires entre les ticks, hors chronométrage.

Ces changements rendent les traces PerfView plus faciles à analyser
et évitent d’attribuer les allocations des marqueurs à la simulation.

Aucune modification des règles de combat ni optimisation du moteur.


# COMMIT 8ff92c9ba83eb5165bf40af0e0ff6e60329a690a # 

## Itération C1.2b — Country Combat Aggregation

### Objectif

Réduire les calculs redondants lors des engagements
concentrant de nombreuses divisions.

### Architecture

Optimisation localisée dans EngagementSystem.

Ajout de CountryCombatSummary et de
BuildCountryCombatSummaries().

### Fonctionnement

Pendant chaque tick d'engagement :

1. Calcul des puissances individuelles.
2. Regroupement des participants actifs par pays.
3. Calcul unique des puissances hostiles par pays.
4. Réutilisation des agrégats pour chaque division.
5. Application des pertes selon le mécanisme existant.

### Compatibilité

- Conservation des règles diplomatiques.
- Conservation du comptage des cibles amies.
- Exclusion des divisions rompues.
- Conservation des sommes et des arrondis.
- Maintien des phases de calcul et d'application.
- Recalcul des agrégats à chaque tick.
- Aucun changement des stocks physiques.

### Tests

- Engagement entre deux pays.
- Trois pays avec hostilités partielles.
- Division ennemie rompue.
- Renfort rejoignant une bataille active.
- Tests de non-régression existants.
- Comparaison des résultats militaires C1.2a/C1.2b.

### Performance

Comparer les scénarios C1.2a et C1.2b :

- 500 divisions dans 250 duels.
- 500 divisions dans un engagement unique.

Mesurer les latences, allocations et états militaires.

Les gains seront consignés après exécution réelle.

### Limites

- Le calcul conserve une liste temporaire par pays.
- Les répartiteurs de pertes ne sont pas optimisés.
- Les agrégations physiques restent répétées.
- La détection et l'indexation des batailles sont inchangées.
- Le comportement reste séquentiel.
- Les batailles tactiques hybrides ne sont pas encore simulées.

### Suite

C1.2c : optimisation des allocations des répartiteurs
de pertes humaines et de chars.

Puis C1.2d : indexation des engagements actifs,
si les benchmarks confirment son intérêt.

# COMMIT cb1a5b4e147f38a3323f929cdf8890bd2da43427 # 

## Itération C1.2c — Hierarchical Loss Allocation Optimization

### Objectif
Réduire les allocations temporaires des répartiteurs
de pertes humaines et de chars.

### Architecture
- Ajout de HierarchicalLossTargetIndex.
- Références des compagnies conservées après
  activation de la hiérarchie.
- Stocks physiques toujours relus dynamiquement.
- Aucun cache permanent des effectifs disponibles.

### Pertes humaines
- Conservation de la répartition proportionnelle.
- Conservation des règles de départage.
- Utilisation de Span<T> et stackalloc.
- Suppression des objets Target temporaires.
- Ajout de ApplyInPlace() pour le moteur.
- Conservation de Apply() pour les rapports détaillés.

### Pertes de chars
- Utilisation de l'index stable des compagnies.
- Chemin rapide sans char opérationnel.
- Chemin rapide sans perte entière.
- Conservation des fractions entre ticks.
- Conservation des priorités de destruction.
- Ajout de ApplyToPools() pour le moteur.
- Conservation de Apply() pour les rapports détaillés.

### Compatibilité
- Aucune modification des coefficients de combat.
- Aucune modification des règles de répartition.
- Aucune modification de la diplomatie.
- Conservation des états historiques des ressources.
- Aucun changement du Battle Ledger.

### Tests
- Comparaison entre API détaillée et API rapide.
- Validation des pertes humaines par compagnie.
- Validation des fractions de pertes blindées.
- Validation des divisions sans chars.
- Vérification du cache structurel.
- Suite complète de non-régression.

### Performances
Comparer C1.2b et C1.2c :
- Durées moyenne et médiane des ticks.
- p95 et maximum.
- Allocations par tick.
- Collections Gen0/Gen1/Gen2.
- États militaires aux bornes des simulations.

### Suite
C1.2d : étudier l'indexation des engagements actifs
par province et par liaison.

Puis C2 : benchmarks opérationnels avec
mouvements, ravitaillement et longues campagnes.

# COMMITS def525e90f5a38034c71b3e87f3706358ea95ea2 & 98c14f9247f6d596d18e3f3104d39ef78a99c6da # 

## C1.2d — Active Engagement Indexing

### Objectif
Améliorer la recherche des engagements actifs
sans modifier les règles de combat.

### Modifications
- Conservation de l'historique complet des engagements.
- Ajout d'une liste ordonnée des engagements actifs.
- Index des batailles actives par province.
- Index des rencontres actives par liaison et sens.
- Enregistrement centralisé des engagements.
- Nettoyage des index lorsqu'un engagement se termine.
- Recherche des renforts limitée aux liaisons concernées.

### Invariants
- Un engagement actif figure dans la liste active.
- Une bataille de province active est indexée par sa province.
- Une rencontre active est accessible depuis les deux sens.
- Les engagements terminés quittent les index actifs.
- Les engagements terminés restent dans l'historique.
- Les identifiants et l'ordre des combats restent inchangés.
- Les règles militaires et diplomatiques sont conservées.

### Tests
- Provinces distinctes partageant un identifiant numérique.
- Batailles successives dans une même province.
- Conservation des archives après la fin d'un engagement.
- Renforts sur une liaison précise.
- Suite complète de non-régression.

### Limites
- Les recherches de nouvelles rencontres entre divisions
  mobiles ne sont pas encore indexées.
- La suppression dans la liste active reste linéaire.
- Les performances des campagnes longues restent
  à mesurer avec un benchmark spécifique.

### Suite
C2 — Benchmarks de charge opérationnelle :
mouvements, logistique, renforts, fins de batailles
et accumulation d'historiques.


### Résultats C1.2d-History

Comparaison contrôlée des moteurs C1.2c et C1.2d.

- 50 engagements actifs.
- 500 divisions en combat.
- Historiques : 0, 1 000, 5 000, 10 000 batailles.
- 24 exécutions comparatives.
- Même code de benchmark entre les versions.

Résultat principal :
- À 10 000 batailles archivées, la recherche
  provinciale passe de 0,880 ms à 0,072 ms.
- Accélération : x12,27.
- Réduction du temps de recherche : 91,9 %.

Validité :
- Charges militaires agrégées conservées.
- Tests de non-régression validés.

Limites :
- Gain global sur le tick non démontré.
- Plateaux temporels encore inexpliqués.
- La campagne de benchmark ne couvre pas
  les mouvements, la logistique et les
  engagements dynamiques.

Conclusion :
Objectif d'indexation atteint.
C1.2d clôturée, passage à C2.


# COMMIT 9bac427097cca77b08598e0616ffe3ef05bf0d43 # 

## C2.1a — Fondations du combat tactique hybride

### Objectif
Introduire la représentation locale d'une bataille,
sans modifier la résolution stratégique existante.

### Architecture
- TacticalBattle référence un Engagement existant.
- TacticalBattlefield représente un graphe de secteurs.
- BattleSector décrit le terrain dominant, l'altitude
  et certaines caractéristiques secondaires.
- SectorConnection décrit les liaisons et distances.
- CompanyDeployment référence une véritable Company.
- Les bataillons sont retrouvés à partir des compagnies.

### Invariants
- Aucun stock de personnel ou de matériel dupliqué.
- Une compagnie n'a qu'un déploiement par bataille.
- Seules les divisions participantes peuvent
  fournir des compagnies à une bataille.
- Les secteurs appartiennent à un champ de bataille.
- Les identifiants de secteur sont uniques
  à l'intérieur d'un champ de bataille.
- Les compagnies conservent leurs parents physiques.
- Les déploiements sont initialement immuables.

### Tests
- Terrains hétérogènes et connexions.
- Rejet des connexions et secteurs invalides.
- Identité et appartenance des compagnies.
- Conservation des ressources physiques.
- Rejet des déploiements multiples et étrangers.

### Limites volontaires
- Aucun calcul de combat tactique.
- Aucun déplacement tactique.
- Aucun commandement automatique pour l'instant.
- Pas encore d'intégration à SimulationEngine.Tick().
- Aucun affichage tactique dans Avalonia.

### Prochaine étape
C2.1b :
- Ordres divisionnaires.
- Répartition automatique des bataillons.
- Affectation des compagnies aux secteurs.
- Gestion des réserves et des priorités tactiques.

# COMMIT d2695a28bb67909354d2c15a34a60e90dd3c53fb # 

## C2.1b — Commandement tactique automatisé

### Objectif
Transformer un ordre divisionnaire en déploiements
automatiques de bataillons et de compagnies.

### Architecture
- DivisionTacticalOrder : ordre donné à la division.
- TacticalPosture : attaque ou défense.
- TacticalCommandSystem : planification automatique.
- TacticalDeploymentPlan : plan inspectable et validable.
- TacticalCompanyAssignment : affectation prévue
  d'une compagnie à un secteur et à un rôle.

### Fonctionnement
- Bataillons traités dans l'ordre de leurs identifiants.
- Répartition sur les secteurs désignés.
- Renforcement du secteur prioritaire.
- Réservation possible de bataillons entiers.
- Répartition des compagnies en première ligne,
  soutien et réserve selon la posture.
- Planification sans mutation des ressources.
- Validation préalable à l'application du plan.

### Invariants
- Seules les divisions participantes sont admises.
- Aucun bataillon ni aucune compagnie n'est dupliqué.
- Les ressources physiques restent dans CompanyComposition.
- Les objectifs doivent appartenir au champ de bataille.
- Un plan périmé est rejeté avant son application.

### Tests
- Répartition selon les priorités.
- Réserves divisionnaires.
- Différence entre attaque et défense.
- Déterminisme de la planification.
- Ordres invalides et plans périmés.

### Limites
- Politique de déploiement provisoire.
- Pas encore de prise en compte détaillée des doctrines.
- Pas encore de sélection fondée sur le matériel.
- Pas de capacité maximale des secteurs.
- Pas de déplacement ou de redéploiement tactique.
- Pas de calcul d'affrontements locaux.
- Pas encore de déclenchement automatique depuis
  SimulationEngine.Tick().

### Prochaine étape
C2.1c : instantanés tactiques permettant à l'interface
de visualiser secteurs, bataillons, compagnies,
postures et réserves.

# COMMIT 233ec28aa655556a4f9b16a2e6dc2ed99d251f06 # 
### C2.1c — Observation des décisions tactiques

### Objectif

Conserver et restituer les décisions réellement appliquées
par le commandement tactique, sans inventer leur justification
dans la future interface.

### Architecture

TacticalAssignmentReason : raison d'affectation d'une compagnie.

TacticalDeploymentPlan conserve l'ordre divisionnaire source.

TacticalBattle conserve les plans effectivement appliqués.

TacticalBattleSnapshotBuilder capture un instantané détaché
des objets vivants du moteur.

TacticalBattleReportFormatter produit un rapport textuel
à partir de cet instantané.

### Fonctionnement

Distinction entre objectif prioritaire, objectif secondaire
et réserve divisionnaire.

Enregistrement du plan après son application complète.

Refus d'un second plan appliqué pour une même division.

Capture des secteurs, terrains, liaisons et ordres.

Observation des compagnies, de leurs bataillons, rôles,
effectifs disponibles et chars opérationnels ou endommagés.

Identification locale des divisions par ParticipantIndex,
sans supposer leurs noms ou les identifiants des compagnies
uniques dans toute la bataille.

Un déploiement manuel conserve une origine non documentée.

### Invariants

Un plan simplement calculé n'est pas présenté comme appliqué.

Les raisons exposées proviennent des affectations enregistrées.

Les instantanés produits ne conservent pas de références
vivantes vers les unités, secteurs ou stocks physiques.

Les valeurs déjà capturées restent indépendantes des
modifications ultérieures du moteur.

Les ressources physiques restent dans les compositions :
les instantanés en copient les valeurs sans créer
une seconde comptabilité.

### Tests ajoutés

Restitution des ordres et des affectations automatiques.

Absence de décision appliquée avant ApplyPlan.

Conservation des effectifs capturés après modification
des stocks physiques.

Absence de justification inventée pour un déploiement manuel.

Distinction de participants partageant des identifiants
de compagnie identiques.

Résultats d'exécution à renseigner après validation locale.

### Intégration

Script Python ponctuel pour adapter TacticalCommandSystem
et TacticalBattle ; aucune dépendance Python à l'exécution.

Correction du nom de fichier :
TacticalBatlle.cs devient TacticalBattle.cs.

### Limites

Aucun calcul d'affrontement tactique ni déplacement local.

Aucun raccordement automatique à SimulationEngine.Tick().

Aucun affichage tactique dans Avalonia à ce stade.

Les raisons décrivent la politique de répartition existante,
pas un raisonnement fondé sur les doctrines ou le matériel.

Les renseignements des deux camps sont exposés pour
le diagnostic, sans filtrage du brouillard de guerre.

# COMMIT aaef01e41fde4757278b829b32cdfdee42f3d0db # 
### C2.1d — Inspection tactique dans Avalonia

Objectif :
Permettre l'observation graphique des déploiements
automatiques produits par le commandement divisionnaire.

## Architecture

TacticalDemoScenario construit une bataille de démonstration
indépendante de la simulation stratégique principale.

Les déploiements sont produits par TacticalCommandSystem.

TacticalBattleSnapshotBuilder capture l'état tactique.

TacticalBattleWindow affiche uniquement cet instantané.

MainWindow propose un bouton « Démo Tactique »

pour ouvrir la fenêtre d'inspection.
Fonctionnalités :
- Fenêtre d'inspection tactique indépendante.
- Sélection des secteurs.
- Affichage des terrains et connexions.
- Regroupement des compagnies par bataillon.
- Identification des divisions et des camps.
- Affichage des rôles : première ligne,
  soutien et réserve.
- Explication des affectations automatiques.
- Lecture exclusive de TacticalBattleSnapshot.

Scénario de validation :
Deux divisions hiérarchiques participent à une
bataille de démonstration indépendante.

Limites :
- Pas encore de combat tactique calculé.
- Pas encore d'intégration aux batailles
  stratégiques réellement simulées.
- Pas de mouvement tactique.
- Pas de filtre de renseignement ennemi.
- Interface d'inspection, non interface finale.

Prochaine étape :
C2.2 — Identification des interactions locales
entre compagnies et premiers modèles d'exposition
au terrain.
 
# COMMIT e29c41d4353fdf649bea2e958818b3a2390da761 #  

## C2.2a — Détection des contacts tactiques potentiels

## Objectif

Introduire un premier système capable d'identifier les
compagnies adverses susceptibles d'interagir au sein
d'une bataille tactique.

Cette itération prépare les futurs calculs d'exposition,
de visibilité et de résolution des affrontements.

## Architecture

Nouveau composant :
TacticalContactDetector

Structures introduites :
- TacticalContactCandidate
- TacticalContactTopology

Deux topologies sont reconnues :
- SameSector : compagnies dans le même secteur.
- ConnectedSectors : compagnies présentes dans deux
  secteurs reliés par une connexion tactique.

Ces relations décrivent une proximité topologique,
et non une capacité de tir confirmée.

## Règles initiales

- Seules les compagnies en première ligne participent
  à la détection des contacts directs.
- Les compagnies sans personnel disponible sont exclues.
- Les deux formations doivent appartenir à des pays
  actuellement en guerre.
- Les réserves et soutiens n'interviennent pas encore
  comme participants directs.
- Les secteurs doivent être identiques ou directement
  connectés.
- Aucune relation n'est dupliquée.

## Choix de conception

Le détecteur reçoit explicitement le système diplomatique.

Les unités sont regroupées localement par secteur,
afin d'éviter une comparaison exhaustive entre toutes
les compagnies de la bataille.

Les objets de contact référencent les déploiements
existants et ne créent aucune nouvelle ressource.

La détection ne modifie pas l'état militaire.

## Limites

- Aucun calcul de tir.
- Aucune perte de personnel ou de matériel.
- Aucun effet de suppression ou d'organisation.
- Pas de ligne de vue effective.
- Pas de portée des armes.
- Pas de position individuelle des compagnies
  à l'intérieur des secteurs.
- Pas encore de mécanisme d'appui tactique.
- Pas de synchronisation avec les ticks stratégiques.
- Pas encore d'affichage des contacts dans Avalonia.

## Validation attendue des tests 

Huit tests couvrent :
- Contacts dans un même secteur.
- Contacts entre secteurs connectés.
- Absence de contact entre secteurs éloignés.
- Exclusion des soutiens et réserves.
- Absence de doublons.
- Vérification de l'état de guerre.
- Exclusion des compagnies sans personnel.
- Conservation de l'état militaire.

## Suite envisagée — C2.2b

Introduire une représentation de l'exposition,
des positions tactiques et de la visibilité.

Distinguer les contacts possibles des contacts
effectivement exploitables pour un engagement.

Étudier les conditions d'intervention des unités
de soutien et des armes à portée indirecte.

# COMMIT 89469af37589d04224826905e15504b8aa22cb86  # 

## C2.2b.1 — Fondations de la géométrie tactique locale

### Objectif

Ajouter une représentation spatiale simplifiée
aux batailles tactiques de WoF.

### Nouvelles structures

- TacticalPoint : coordonnées locales X/Y et altitude.
- SectorGeometry : centre et rayon approximatif
  d'un secteur.
- CompanyTacticalPosition : position, orientation
  et largeur de front d'une compagnie.
- TacticalGeometryMap : gestion des géométries
  et positions associées à une bataille.
- TacticalSpatialRelation : distances et angles
  relatifs entre deux formations.

### Fonctionnement

- Repère local en mètres.
- X vers l'est, Y vers le nord.
- Orientation : 0° nord, 90° est.
- Position unique par déploiement.
- Validation des limites approximatives du secteur.
- Distance horizontale entre compagnies.
- Différence d'altitude signée.
- Distance spatiale en ligne droite.
- Direction absolue et relative des formations.

### Invariants

- Aucune duplication de compagnie.
- Aucun nouveau stock de personnel ou de matériel.
- Une position référence un déploiement existant.
- Les coordonnées invalides sont rejetées.
- Les déploiements étrangers sont rejetés.
- Les opérations géométriques ne modifient pas
  les ressources militaires.

### Limites

- Secteurs approximés par des cercles.
- Aucun obstacle géométrique.
- Aucun calcul de ligne de vue.
- Pas de portée d'arme.
- Pas de mouvement tactique.
- Pas de contrôle des chevauchements de fronts.
- Pas encore de placement spatial automatisé.
- Pas encore de raccordement au détecteur C2.2a.
- Pas encore d'affichage des positions dans Avalonia.

### Tests prévus

- Validation des coordonnées et des secteurs.
- Vérification des appartenances géographiques.
- Unicité des positions.
- Exclusion des déploiements étrangers.
- Calcul des distances et orientations.
- Conservation de l'état militaire.

### Prochaine étape : C2.2b.2

Ajouter des obstacles géométriques simplifiés
et déterminer si un corridor d'observation est
bloqué par le relief ou par un obstacle connu.

# COMMIT 37cedb3a4d1c0f74f9ba39731f031d43f15d25e5 #  
## Obstacles géométriques et corridors d'observation

### Contexte

La phase C2 développe un moteur de combat tactique
hybride, fondé sur des secteurs et des compagnies.

C2.2a a introduit la détection de contacts potentiels.
C2.2b.1 a ajouté les positions géométriques locales
des compagnies, leurs orientations et les distances.

C2.2b.2 permet d'évaluer si un obstacle géométrique
connu masque un corridor entre deux positions.

## Objectifs

- Représenter les premiers obstacles spatiaux.
- Identifier leur intersection avec un corridor.
- Tenir compte des altitudes des observateurs.
- Déterminer si l'obstacle masque ce corridor.
- Identifier l'obstacle bloquant le plus proche.
- Conserver un système sans mutation militaire.

## Nouvelles structures

### TacticalLineObstacle

Obstacle modélisé par un segment horizontal
avec une altitude supérieure absolue.

Types initialement prévus :
- Ridge : crête ou obstacle topographique.
- Wall : mur ou structure opaque.

Les coordonnées sont exprimées en mètres,
dans le repère local de la bataille.

### TacticalSightEvaluator

Effectue l'évaluation géométrique entre deux
déploiements possédant des positions spatiales.

Résultats possibles :

- Unknown
- ClearOfKnownObstacles
- BlockedByObstacle

Le calcul distingue l'absence d'obstacle connu
de la visibilité réelle d'un adversaire.

### TacticalSightResult

Conserve :
- Le statut géométrique du corridor.
- La distance horizontale entre les formations.
- L'obstacle bloquant, lorsqu'il existe.
- Sa distance depuis l'observateur.

## Fonctionnement géométrique

La ligne d'observation relie deux points situés
au-dessus du sol, à une hauteur configurable.

La hauteur de la ligne est interpolée
entre l'observateur et la cible.

Les obstacles sont examinés pour déterminer
leur intersection avec cette trajectoire.

Lorsqu'un obstacle atteint ou dépasse
la hauteur du corridor, ce dernier est bloqué.

Les segments colinéaires sont également traités.

En cas de plusieurs obstructions, la plus proche
de l'observateur est retenue.

## Choix architecturaux

La géométrie reste indépendante des stocks
de personnel et de matériel.

Les obstacles sont associés à une
TacticalGeometryMap existante.

Le détecteur de contacts tactiques C2.2a
n'est pas encore modifié.

L'évaluation reste un calcul géométrique pur
et ne provoque aucun tir ni aucune perte.

## Limites du modèle

Les crêtes sont représentées par des segments
de hauteur constante, non par des profils
topographiques complets.

Les coordonnées des obstacles sont explicites :
elles ne sont pas encore générées automatiquement.

Les attributs de terrain d'un secteur ne créent
pas automatiquement des obstacles.

La végétation et les bâtiments complexes
ne possèdent pas encore de géométrie volumique.

Une compagnie est représentée par un point
d'observation, pas par chaque soldat.

Aucun modèle de météo, fumée, camouflage,
éclairage ou reconnaissance n'est appliqué.

L'orientation des compagnies ne limite pas
encore l'observation.

ClearOfKnownObstacles ne signifie pas que
l'adversaire est effectivement visible.

Le calcul n'est pas encore intégré aux contacts
candidats de C2.2a.

Aucun résultat n'est encore affiché dans Avalonia.

## Tests prévus

- Crête bloquant un corridor.
- Obstacle trop bas pour masquer la cible.
- Obstacle situé hors trajectoire.
- Identification de l'obstacle le plus proche.
- Interpolation sur terrain en pente.
- Obstacle colinéaire.
- Positions manquantes ou confondues.
- Validation des obstacles et hauteurs.
- Déterminisme et conservation des ressources.

## Prochaine itération — C2.2b.3

Raccorder TacticalSightEvaluator
à TacticalContactDetector.

Pour chaque contact candidat :
- Identifier les positions géométriques.
- Mesurer les distances.
- Évaluer les obstacles connus.
- Produire une relation tactique enrichie.
- Distinguer les corridors bloqués des corridors
  potentiellement exploitables.

Ce raccordement ne déclenchera pas encore
d'échanges de tirs ni de pertes.

# COMMIT 6048732741c6f01e0fe3822cdaa3fe3b67b5ecb0 # 

## C2.2b.3 — Évaluation géométrique des contacts tactiques

### Fonctionnalités
- Raccordement de la détection des contacts (C2.2a)
  aux positions et obstacles géométriques (C2.2b).
- Calcul des distances, altitudes et orientations
  relatives entre compagnies adverses.
- Évaluation des corridors d'observation dans les
  deux directions (A → B et B → A).
- Identification des obstacles bloquants.
- Conservation des contacts dont la géométrie
  est inconnue, sans supposer de visibilité.
- Aucune modification des ressources militaires.

### Architecture
- `TacticalContactEvaluator` : orchestre la détection
  et l'évaluation spatiale.
- `TacticalEvaluatedContact` : regroupe le contact
  initial, la relation spatiale et les deux résultats
  d'observation.
- `TacticalGeometryMap.Battle` : vérifie l'appartenance
  de la géométrie à la bataille évaluée.
- Réutilisation des composants existants sans modifier
  leurs responsabilités.

### Validation
- 10 nouveaux tests réussis.
- Suite de tests validée.
- Aucun calcul de tir ou de pertes introduit.

### Limites
- Corridor dégagé ≠ ennemi effectivement détecté.
- Portée des armes, camouflage et reconnaissance
  non encore pris en compte.
- Pas encore d'intégration aux ticks stratégiques.

### Prochaine étape
Introduire la perception tactique des formations :
distinguer les positions connues du moteur des
informations réellement accessibles aux unités.

# COMMIT fbcb68ff3aa9e8c90751d46084e5f5e99f6bced3 # 

## C2.2c.1 — Première mémoire de perception tactique

### Fonctionnalités
- Acquisition provisoire de contacts ennemis selon
  la distance, l'orientation et les obstacles connus.
- Observation indépendante dans chaque direction.
- Mémoire des dernières positions observées.
- Conservation des contacts perdus sous forme de
  pistes périmées, avec heure de dernière observation.
- Réacquisition d'une cible sans créer de nouvelle piste.
- Pas de partage automatique entre compagnies.

### Architecture
- `TacticalObservationProfile` : paramètres
  de portée et de champ de vision.
- `TacticalPerceptionSystem` : exploite les
  contacts géométriques et actualise la perception.
- `TacticalPerceptionState` : conserve les pistes
  séparément pour chaque compagnie observatrice.
- `TacticalEnemyContactReport` : expose uniquement
  un identifiant de piste, une dernière position
  connue, une heure et un état d'observation.
- Les références aux véritables cibles restent
  internes à la mémoire du moteur.
- Aucun changement au moteur de combat existant.

### Tests — intérêt
- Orientation et observation réciproque :
  empêchent de supposer qu'un contact est
  automatiquement connu des deux camps.
- Portée, obstacles et géométrie manquante :
  vérifient qu'aucune information n'est inventée.
- Perte et réacquisition de contact :
  vérifient la conservation temporelle des pistes.
- Isolation des observateurs et des batailles :
  empêchent les échanges involontaires de données.
- Déterminisme et conservation des ressources :
  garantissent une observation sans effets militaires.

### Limites
- Règles d'observation déterministes et provisoires.
- Camouflage, météo, reconnaissance et identification
  détaillée non implémentés.
- Détection limitée aux candidats de première ligne.
- Pas de partage de renseignements entre unités.
- Pas d'intégration automatique aux ticks.
- Pas encore de filtrage des informations dans Avalonia.

### Suite
Introduire des règles de détection plus approfondies,
puis les mécanismes contrôlés de transmission des
renseignements entre formations.
