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


# COMMIT # 

-Le réseau logistique ne repose plus uniquement sur des liaisons abstraites définies par une capacité arbitraire.
Les SupplyRoute possèdent désormais un type d’infrastructure et un niveau, à partir desquels leur capacité de transport est calculée automatiquement.

ajout de InfrastructureType ;
distinction entre route et voie ferrée ;
ajout d’un niveau d’infrastructure ;
calcul automatique de la capacité à partir du type et du niveau ;
conservation de la compatibilité avec l’algorithme de recherche du meilleur chemin ;
ajout de tests vérifiant qu’une voie ferrée transporte davantage qu’une route de même niveau ;
ajout de tests vérifiant qu’une infrastructure de niveau supérieur possède une capacité plus élevée.


# COMMIT #

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




