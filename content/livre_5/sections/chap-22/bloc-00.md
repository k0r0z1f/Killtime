---
cible: sections.chap-22.blocs.0.html
format: raw
livre: 5
---


                    <p>
                        Ici, pas de jauge de PV abstraite. Chaque coup touche un endroit précis —
                        bras, tête, jambes — avec un effet direct. Tu vises, tu touches, et le corps <a class="rule-ref" href="livre_6.html#sec-23-2" data-tip="Encaissement = CON × 2 : seuil comparé aux dégâts du coup (§23.2).">encaisse</a> ou casse.
                    </p>

                    <h3 id="sec-22-1">22.1 Cible Visée, Niveaux de Visée &amp; Différentiel de Touche</h3>
                    <p>
                        Chaque attaque vise une zone précise du corps — sans aucune déviation aléatoire : tu touches là où tu vises, ou tu rates. Annonce ta cible avant de lancer, en choisissant entre deux niveaux de précision :
                    </p>
                    <ul>
                        <li><strong>Niveau 1 — Les 6 zones larges (du haut au bas, malus 0) :</strong>
                            <strong>Tête / Cou</strong>, <strong>Torse / Dos</strong> <em>(zone par défaut)</em>, <strong>Bras gauche</strong>, <strong>Bras droit</strong>, <strong>Jambe gauche</strong>, <strong>Jambe droite</strong>.
                            Ce premier niveau n'impose aucun malus de visée et <strong>ne déclenche aucun effet supplémentaire de base</strong> : il sert à localiser les dégâts pour le seuil d'<a class="rule-ref" href="livre_6.html#sec-23-encaissement" data-tip="Choc Traumatique par zone : table des effets dédiés lorsque les dégâts de la zone dépassent CON×2 (§23).">Encaissement</a> (<code>CON × 2</code> par zone) et l'arbre de chute à 0 PV (<a class="rule-ref" href="livre_6.html#sec-23-2" data-tip="Chute à 0 PV : Encaissement CON×2, Sombrer ou Dernier Souffle (§23.2).">Livre VI §23.2</a>).
                            Dès que les dégâts cumulés sur une zone dépassent strictement <code>CON × 2</code>, le blessé effectue gratuitement (<code>0 PA</code>) une épreuve d'<a class="rule-ref" href="livre_3.html#chap-12" data-tip="Endurance Physique : encaisser, tenir l'effort (CON/FOR, §12).">Endurance Physique</a> (mises d'effort autorisées) contre une <strong>Difficulté de 3, plus 1 par point de dégât au-dessus du seuil</strong> (ex. Encaissement 6 et 8 dégâts sur la zone = Difficulté 5) : en cas de réussite, aucun autre jet sur cette zone tant qu'elle ne subit pas de nouveaux dégâts ; en cas d'échec, l'effet de la zone s'applique jusqu'au prochain soin (<a href="livre_6.html#sec-23-encaissement">table des 6 zones, Livre VI §23</a> — Tête/Cou : <em>Étourdi</em>, Torse/Dos : <em>Déstabilisé</em>, Bras : <em>inutilisable</em>, Jambe : <em>Ralenti</em>).</li>
                        <li><strong>Niveau 2 — Visée chirurgicale (points vitaux, avec malus) :</strong>
                            Tu cibles un point vital précis à l'intérieur d'une des 6 zones (yeux, trachée, cœur, poumons, artère, genou, etc.). Cette précision impose une <strong>punition de visée</strong> de <strong>−1</strong> (point vital standard) ou <strong>−2</strong> (point vital critique/exigu) sur l'<a class="rule-ref" href="livre_2.html#chap-7" data-tip="Épreuve : lance ton dé de compétence + PA − malus contre le seuil du MJ (§7.1).">épreuve</a> — chaque point de malus peut être annulé au préalable en dépensant <strong>+1 <a class="rule-ref" href="livre_1.html#chap-4" data-tip="PA : Max(Agi,Int) + Rap + Min. Total = Tirage + PA − malus, sans bonus de carac (§4).">PA</a></strong> (soit +1 PA pour −1, ou +2 PA pour −2). <strong>Seul le Niveau 2 déclenche les effets supplémentaires ci-dessous.</strong></li>
                        <li><strong>Armure globale &amp; Couvert :</strong> l'armure protège tout le corps avec la même valeur par défaut (à la discrétion du joueur ou du MJ si une pièce est volontairement retirée, ex. pas de casque sur <em>Tête / Cou</em>). Le <a class="rule-ref" href="#sec-21-3" data-tip="Couvert (§21.3) : moitié −1, trois-quarts −2, total impossible. S'applique au jet d'attaque.">couvert</a> applique simplement son malus global au jet d'attaque, sans bloquer ni protéger de zones particulières.</li>
                        <li><strong>Différentiel Positif (Attaque &gt; Défense) :</strong> Touche franche !
                            L'attaque inflige ses dégâts de base augmentés du différentiel net, puis on soustrait l'armure (si au moins 1 dégât passe l'armure, l'effet supplémentaire de Niveau 2 s'applique ; sinon, seuls les effets de sort, pouvoir, don ou spécialisation passent).</li>
                        <li><strong>Différentiel Égal à 0 (Attaque = Défense) :</strong> Le coup touche
                            la zone visée mais sans pénétrer : les dégâts sont ignorés.
                            Les seuls effets qui s'activent sont ceux de spécialisation, de don, de pouvoir ou de sort ; les effets supplémentaires de visée d'un point vital (Niveau 2) ne s'appliquent pas.</li>
                        <li><strong>Différentiel Négatif :</strong> Le coup est totalement esquivé ou paré :
                            aucun dégât, visée manquée.</li>
                        <li><strong>Poings et Pieds (<a class="rule-ref" href="livre_3.html#chap-12" data-tip="Mains Nues : combat à mains nues (FOR/AGI, §12.1).">Mains Nues</a>) :</strong> Tes poings n'ont pas de
                            dégâts de base : touche franche = <strong>0 + différentiel</strong>. Mais le coup secoue :
                            la cible prend aussi <strong>1 point d'<a class="rule-ref" href="livre_1.html#sec-4-2" data-tip="Essoufflement : 1 point = des PA d'urgence ; ne part pas tout seul, action dédiée pour souffler (§4.2).">Essoufflement</a></strong>.</li>
                    </ul>

                    
