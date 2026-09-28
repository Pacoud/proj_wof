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


# COMMIT # 47da8f214851e333d5e22797109ae7498c3f69b0 
-recherche du chemin offrant la meilleure capacité logistique ;
-prise en compte du maillon le plus faible de chaque trajet ;
-utilisation des capacités restantes pendant le tick, et non uniquement des capacités nominales ;
-possibilité d’utiliser automatiquement un chemin alternatif lorsqu’une branche du réseau est saturée ;
-partage des capacités du réseau entre plusieurs divisions ;
-ajout de tests validant le choix du meilleur trajet et l’utilisation d’itinéraires alternatifs.


# COMMIT 2c3930d3553cd4600083ad2f3e8b4c0d079ee734 # 

-Le réseau logistique ne repose plus uniquement sur des liaisons abstraites définies par une capacité arbitraire.
Les SupplyRoute possèdent désormais un type d’infrastructure et un niveau, à partir desquels leur capacité de transport est calculée automatiquement.

ajout de InfrastructureType ;
distinction entre route et voie ferrée ;
ajout d’un niveau d’infrastructure ;
calcul automatique de la capacité à partir du type et du niveau ;
conservation de la compatibilité avec l’algorithme de recherche du meilleur chemin ;
ajout de tests vérifiant qu’une voie ferrée transporte davantage qu’une route de même niveau ;
ajout de tests vérifiant qu’une infrastructure de niveau supérieur possède une capacité plus élevée.


# COMMIT 3e4775dc26bb00d8a0533110e712325739d21adc #

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

# COMMIT # 
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

# COMMIT # 
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





