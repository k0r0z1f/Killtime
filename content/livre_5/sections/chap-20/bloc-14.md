---
cible: sections.chap-20.blocs.14.html
format: raw
livre: 5
---


                    <ul>
                        <li><strong>⚡ Impact de Charge (+2 dégâts) :</strong> le bonus s'applique aux dégâts de l'attaque
                            de contact (mêlée, mains nues, arme blanche ou contondante). Il est consommé dès la résolution de la frappe.
                            Il n'augmente pas les tirs à distance.</li>
                        <li><strong>🛡️ Exposition défensive (-1 défense jusqu'au prochain tour) :</strong> le chargeur s'expose
                            en brisant sa garde pour maximiser son élan. Tout <a class="rule-ref" href="livre_2.html#chap-7" data-tip="Face-à-Face : chacun lance son dé + PA, le meilleur gagne. Seul le vainqueur coche (§7.3).">Face-à-Face</a> où il se défend (parade, esquive active ou réflexe)
                            subit un malus de <strong>-1</strong>. Ce malus persiste pendant le reste de son tour et durant les tours
                            adverses, jusqu'au début de son propre tour personnel suivant (recalage à <code>ResetTurn</code>).</li>
                        <li><strong>🏃 Verrouillage du Tir (« Sprint bloque tir ce tour ») :</strong> courir à pleine vitesse
                            (3 cases ou plus) rompt la posture de visée. Toute arme à distance (pistolets, fusils, arcs,
                            armes de jet) est neutralisée pour le reste du tour en cours. La frappe au corps-à-corps reste
                            pleinement accessible.</li>
                        <li><strong>🧭 Pathfinder & Exécution :</strong> l'algorithme <code>HexPathfinder</code> calcule
                            les trajectoires d'assaut vers les flancs libres de la cible (<code>FindChargePath</code>).
                            L'action contextuelle <strong>⚡ Charge au contact</strong> permet d'enchaîner en un clic
                            la course d'approche et la frappe chirurgicale au Torse.</li>
                    </ul>

                    <h3 id="sec-20-8">20.8 Cran, Déroute & Reddition</h3>
                    <p>
                        Tu prends un mauvais coup. Ton sang couvre le sol. Tes jambes tremblent.
                        C'est là que tu découvres si tu tiens ou si tu craques.
                        Un seul test par combat. Après, ton sort est scellé.
                    </p>
                    <ul>
                        <li><strong>Déclencheur (28 % de tes PV max) :</strong> quand tes dégâts accumulés
                            font passer tes PV restants <strong>à ou sous le plafond de 28 % du max</strong>
                            (max = <a class="rule-ref" href="livre_1.html#chap-4" data-tip="PV max = CON × 5. Seuil létal (§4).">CON × 5</a>),
                            tu fais immédiatement ton jet de Cran.
                            Exemple : CON 4 → max 20 → seuil 6 (20 × 0,28 = 5,6, arrondi au-dessus).
                            À 6 PV ou moins, tu testes. Une seule fois par combat.
                            Si un soin te remonte au-dessus du seuil, ton cran se réarme.
                            Pas de test si tu es pulvérisé, <a class="rule-ref" href="livre_6.html#sec-24-5" data-tip="Inconscient : aucune action ni réaction. Réveil par secousse vigoureuse (1 PA) ou premiers soins.">Inconscient</a>,
                            mort, ou déjà testé.</li>
                        <li><strong>Jet de Cran (compétence, jamais de carac brute) :</strong> tu choisis
                            ton meilleur outil entre <strong>Mener</strong> et <strong>Intuition</strong>
                            (<a class="rule-ref" href="livre_3.html#chap-13" data-tip="Mener : commander, rallier, tenir une ligne (Leadership, §13.2). Intuition : flair, sang-froid, lecture du danger (§13.3).">Livre III §13</a>).
                            Tu lances ton dé + <a class="rule-ref" href="livre_1.html#chap-4" data-tip="PA : Max(Agi,Int) + Rap + Min. Total = Tirage + PA − malus, sans bonus de carac (§4).">PA</a> injectés
                            − malus contre <strong>SD 10</strong>.
                            Exemple : tu as Intuition d8, tu injectes 3 PA, tirage 7 → 7 + 3 = 10, tu tiens.
                            Réussite = tu restes en jeu et tu coches ta progression comme après toute épreuve.
                            Échec = déroute (ci-dessous).</li>
                        <li><strong>Échec = Déroute (même règle pour tous) :</strong> tu gagnes
                            <a class="rule-ref" href="livre_6.html#sec-24-1" data-tip="Agonisant : aucune action raisonnée, fuite de 6 cases (1d8). Persiste jusqu’à stabilisation médicale."><strong>Agonisant (déroute)</strong></a>.
                            Tu fuis le danger en panique, −2 à tes jets, sans action raisonnée.
                            Un allié au chevet peut te stabiliser aux
                            <a class="rule-ref" href="livre_6.html#chap-25" data-tip="Premiers Soins (3 PA, dif. 12) : stabilise un Agonisant en Inconscient stable (Livre VI §25.1).">Premiers Soins (3 PA, dif. 12)</a>
                            vers un <a class="rule-ref" href="livre_6.html#sec-24-5" data-tip="Inconscient : aucune action ni réaction. Réveil par secousse vigoureuse (1 PA) ou premiers soins.">Inconscient stable</a>.
                            Ne confonds pas avec le <a class="rule-ref" href="livre_6.html#sec-23-2" data-tip="Dernier Souffle : à 0 PV (coup sous Encaissement), reste conscient et agis, +1 Essoufflement par jet (§23.2).">Dernier Souffle</a> :
                            lui, c'est ton choix héroïque à 0 PV. La déroute, elle, te choisit.</li>
                        <li><strong>Reddition = ton choix, pas un jet :</strong> à ton tour, tu peux déposer
                            les armes au sol, mains visibles, et cesser le combat.
                            Ton tour s'arrête. Les ennemis te sortent de leur ciblage offensif
                            — sauf bête affamée ou tueur méthodique, ton MJ tranche.
                            Reprendre le combat coûte 1 <a class="rule-ref" href="livre_1.html#chap-4" data-tip="PA : Max(Agi,Int) + Rap + Min. Total = Tirage + PA − malus, sans bonus de carac (§4).">PA</a>
                            pour ramasser ton arme, avec l'accord du MJ.
                            Le MJ propose la même sortie aux PNJ quand leur cause est perdue.</li>
                        <li><strong>Rattrapage :</strong> le don Humain <strong>E Sang-Froid</strong>
                            (1×/session : ignore un échec de moral) et l'événement
                            <strong>Miracle inexpliqué</strong> (Livre VII : ignore un échec de moral)
                            annulent une Déroute après le jet. La spé <strong>Mener : Tenir la Ligne !</strong>
                            (<a class="rule-ref" href="#sec-20-5" data-tip="Garde du corps : au contact d'un allié attaqué, prend le coup à sa place pour 1 PA (§20.5).">§20.5</a>)
                            reste ton meilleur rempart collectif : un chef qui tient aide tout le groupe à tenir.</li>
                    </ul>

                    <!-- ================= CHAPITRE 24 ================= -->
                    
