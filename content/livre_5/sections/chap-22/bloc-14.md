---
cible: sections.chap-22.blocs.14.html
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

                    <!-- ================= CHAPITRE 26 ================= -->
                    
