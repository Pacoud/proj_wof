Ce projet est une ambition de création de zero d'un jeu de simultation avancé 


--commit -- b7cabff7ed2690a999eb44053939ef7ce7dbca74 --  
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