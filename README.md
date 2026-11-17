Ce projet est une ambition de création de zero d'un jeu de simultation avancé 



## COMMANDES ## 


Client graphique Avalonia -- 
Lancer l'interface graphique Avalonia :

dotnet run --project src/WoF.Client/WoF.Client.csproj

Lance le client console  :
dotnet run --project src/WoF.Prototype/WoF.Prototype.csproj

Pour lancer les tests : 
dotnet test 


## Clôture du bloc OPT-C2.4d.8d.5b

Le détecteur de contacts mobiles est stabilisé sur la version 5b.8. Les tris LINQ conservent l'ordre des rencontres et des premiers contacts.

### Optimisations retenues

- **5b.1** — Le détecteur retire les enveloppes expirées et examine les paires candidates dans un seul parcours de la liste active.
- **5b.6** — La première passe publie les rencontres prédites. Les passes suivantes continuent à chercher les premiers contacts tant que des trajectoires mobiles restent à examiner.
- **5b.8** — À partir de la deuxième passe, la stabilisation termine sans appeler le détecteur si tous les segments sont immobiles. Cette dernière passe reste comptée dans `ResolutionPasses`.

### Expériences écartées

- **5b.2** — Le précalcul des vitesses et des pays a accru les allocations et la latence du scénario dense mesuré.
- **5b.4** — Les tris `Array.Sort` ont réduit les allocations, avec des résultats de latence variables selon les essais.
- **5b.9** — Les deux listes `active` et `activeMoving` ont réduit le nombre de candidats de 27 à 31 %, avec des résultats de latence variables en dense et une légère hausse des allocations de détection. Les tests sur les trajectoires immobiles et les deux ordres de parcours mobile/immobile sont conservés.

### Vérification de clôture

Le build Release a réussi avec zéro avertissement et les 597 tests passent. L'audit NuGet a été désactivé pour le build local, le service distant étant inaccessible lors de la campagne précédente. Les deux benchmarks `full` utilisent `--phases`, une mobilité demandée de 60 % et la graine `12345`. Les échauffements sont de 20 ticks à 500 compagnies et de 10 à 1 000.

| Scénario | Mesures valides | Candidats | Comparaisons exactes | Premiers contacts | Contacts confirmés | Passes | Appels de détection |
|---|---:|---:|---:|---:|---:|---:|---:|
| 500 dense | 40 | 113 406 | 39 438 | 27 531 | 23 562 | 2 | 1 |
| 1 000 dense | 20 | 452 250 | 156 250 | 109 375 | 93 750 | 2 | 1 |

Les deux scénarios reproduisent les compteurs de 5b.8. Les comparaisons détaillées retrouvent les mêmes premiers contacts dans le même ordre, aux mêmes temps et positions, ainsi que les mêmes mouvements, salves, consommations de munitions et pertes. Les exports JSON et CSV sont conservés localement dans `benchmark-results/tactical-mobile/c2-4d-8d-5b-cloture/`.
