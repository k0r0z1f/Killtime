---
cible: sections.chap-20.blocs.0.html
format: raw
livre: 5
---


                    <p>
                        Un cycle dure <strong>10 secondes</strong>. À l'intérieur, chacun joue à son tour,
                        dans l'ordre d'<a class="rule-ref" href="livre_1.html#sec-4-3" data-tip="Initiative : meilleur score entre Rapidité, Agilité et Intelligence, converti en dé (§4.3).">initiative</a>. Quand c'est ton tour, tu dépenses tes <a class="rule-ref" href="livre_1.html#chap-4" data-tip="PA : Max(Agi,Int) + Rap + Min. Total = Tirage + PA − malus, sans bonus de carac (§4).">PA</a> : tu avances,
                        tu frappes, tu ajustes ton tir, tu lances un sort. Et tu en gardes en réserve :
                        parer, ça se fait surtout hors de ton tour, en réaction quand on te tombe dessus.
                        <strong>Tu récupères tous tes PA au début de ton propre tour</strong>
                        (remise au maximum, sans cumul). Pas de recharge globale au début du cycle.
                    </p>

                    <div class="callout temporal">
                        <div class="callout-header">
                            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                                stroke-width="2">
                                <circle cx="12" cy="12" r="10" />
                                <polyline points="12 6 12 12 14 14" />
                            </svg>
                            Séquence Fondamentale du Tour de Combat
                        </div>
                        <div class="callout-body">
                            <ol style="margin-bottom: 0;">
                                <li><strong>Initiative (une seule fois, avant le round 1) :</strong> Jet d'<a class="rule-ref" href="livre_1.html#sec-4-3" data-tip="Initiative : meilleur score entre Rapidité, Agilité et Intelligence, converti en dé (§4.3).">initiative</a> basé sur ton meilleur score entre
                                    <em>Rapidité</em>, <em>Agilité</em> et <em>Intelligence</em> (converti en dé). Égalité : héros avant PNJ, puis plus gros dé, puis choix des joueurs (héros) ou du MJ (PNJ) — voir <a class="rule-ref" href="livre_1.html#sec-4-3" data-tip="Initiative : meilleur score entre Rapidité, Agilité et Intelligence, converti en dé (§4.3).">§4.3</a>. L'ordre reste fixe pour tout le combat.</li>
                                <li><strong>Début de ton tour :</strong> quand ton initiative arrive, dans l'ordre :
                                    tes <strong>résiduels te frappent puis décroissent</strong>, tu <strong>récupères
                                    l'intégralité de ta réserve de <a class="rule-ref" href="livre_1.html#chap-4" data-tip="PA : Max(Agi,Int) + Rap + Min. Total = Tirage + PA − malus, sans bonus de carac (§4).">PA</a> (remise au maximum, sans cumul)</strong>,
                                    et tu <strong>recharges ta réaction libre</strong>.</li>
                                <li><strong>Milieu (tes actions, ordre libre) :</strong> tu agis (action principale, déplacements,
                                    sorts, postures) ; hors de ton tour, les autres peuvent parer et esquiver <strong>sans maximum</strong>
                                    (tant qu'ils paient) ou dépenser leur réaction libre.</li>
                                <li><strong>Fin de ton tour :</strong> expirent les effets ancrés « fin du tour »
                                    (guet, Décrochage, états « jusqu'à la fin du prochain tour »).</li>
                                <li><strong>Itération :</strong> Passer au combattant suivant dans l'ordre d'initiative.
                                    Chaque combattant ne recharge PA et réaction libre qu'au <strong>début de son propre tour</strong>,
                                    jamais au début du cycle global.</li>
                            </ol>
                        </div>
                    </div>

                    <h3 id="sec-20-1">20.1 Postures</h3>
                    <p>
                        Ta posture, c'est comment tu te tiens : garde haute et mobile, penché sur l'ennemi, collé à ton allié pour le couvrir.
                        Elle ne coûte rien à prendre, mais elle te suit partout : chaque posture aide d'un côté et expose de l'autre.
                        Tu veux frapper ? Tu t'ouvres. Tu veux encaisser ? Tu te fermes. Dis ce que tu fais ce tour-ci, ta posture le montre.
                    </p>
                    <p>
                        Choisis ta posture pendant ton tour : une seule à la fois, gratuitement. Elle te suit jusqu'à ce que tu en changes.
                    </p>
                    <ul>
                        <li><strong>Défaut :</strong> aucun bonus ni malus.</li>
                        <li><strong>Préventive :</strong> requise pour la <a class="rule-ref" href="#sec-20-6" data-tip="Prise de coup : au contact d'un allié attaqué, prend le coup à sa place pour 1 PA (§20.6).">prise de coup</a> ; −1 en attaque.</li>
                        <li><strong>Défensive :</strong> +1 en défense, −1 en attaque.</li>
                        <li><strong>Offensive :</strong> +1 en attaque, −1 en défense.</li>
                    </ul>

                    <h3 id="sec-20-2">20.2 L'Économie des Points d'Action : 1 PA = +1 au Résultat</h3>
                    <p>
                        Tes <a class="rule-ref" href="livre_1.html#chap-4" data-tip="PA : Max(Agi,Int) + Rap + Min. Total = Tirage + PA − malus, sans bonus de carac (§4).">PA</a> ne paient pas que tes actions. Tu peux aussi les mettre directement dans ton jet :
                        <strong>1 <a class="rule-ref" href="livre_1.html#chap-4" data-tip="PA : Max(Agi,Int) + Rap + Min. Total = Tirage + PA − malus, sans bonus de carac (§4).">PA</a> dépensé = +1 au résultat</strong>. Tu veux assurer ton coup ? Tu rajoutes des <a class="rule-ref" href="livre_1.html#chap-4" data-tip="PA : Max(Agi,Int) + Rap + Min. Total = Tirage + PA − malus, sans bonus de carac (§4).">PA</a>.
                        Tu veux garder de quoi parer derrière ? Tu en gardes.
                    </p>

                    <div class="formula-box wide">
                        <div class="formula-label">Face-à-Face : Déclarations Masquées puis Révélation Simultanée</div>
                        <div class="formula-math">
                            <span class="hl-cyan">Mise Attaquant</span> (PA + PE, cachée) &nbsp;vs&nbsp; <span
                                class="hl-ruby">Mise Défenseur</span> (PA + PE, cachée) &nbsp;➔&nbsp; Révélation simultanée
                        </div>
                        <div class="formula-desc">
                            <strong>Axiome de Confrontation :</strong> L'attaque oppose une compétence offensive (<em>Maniement d'Arme</em>, <em>Ballistique</em>, <em>Mains Nues</em>) à une compétence défensive (<em>Esquive</em>, <em>Défense Corporelle</em>) ou à une cible sans défense, en <a class="rule-ref" href="livre_2.html#chap-7" data-tip="Face-à-Face : chacun lance son dé + PA, le meilleur gagne. Seul le vainqueur coche (§7.3).">Face-à-Face</a>.<br>
                            Chaque <a class="rule-ref" href="livre_1.html#chap-4" data-tip="PA : Max(Agi,Int) + Rap + Min. Total = Tirage + PA − malus, sans bonus de carac (§4).">PA</a> engagé confère <strong>+1 point direct</strong> au résultat du jet respectif, chaque PE (<a class="rule-ref" href="livre_1.html#sec-4-2" data-tip="Essoufflement : 1 point = des PA d'urgence ; ne part pas tout seul, action dédiée pour souffler (§4.2).">Essoufflement</a>, plafond Constitution) <strong>+1 point direct</strong> — et les deux mises sont <strong>engagées en jetons avant tout jet</strong>. Tu poses ta mise dans ton poing fermé : 1 jeton = 1 <a class="rule-ref" href="livre_1.html#chap-4" data-tip="PA : Max(Agi,Int) + Rap + Min. Total = Tirage + PA − malus, sans bonus de carac (§4).">PA</a> bonus, 1 jeton d'une autre couleur = 1 PE. Pas de jetons sous la main ? Un papier avec le chiffre, ou des dés cachés sous un gobelet. Tu caches aussi ta réserve : tes jetons restants dorment dans ton autre main fermée, ou derrière ton écran. Ton adversaire ne voit rien, ni ta mise ni ce qu'il te reste. Vous ouvrez les deux poings de mise en même temps. Ce que tu montres, tu le paies aussitôt. Aucun résultat n'est révélé avant que les deux camps aient engagé leur mise. La séquence se déroule impérativement en 3 temps :<br>
                            <strong>1. Déclaration de l'Attaquant :</strong> L'attaquant désigne sa cible, annonce son action (compétence + zone visée) et engage sa mise : coût de base du coup (2
                            PA standard, ou 3 PA pour annuler le malus de visée) + PA bonus et/ou PE en jetons dans le poing (débités aussitôt à l'ouverture, +1 / PA, +1 / PE). <em>Exemple : Mains Nues +2 <a class="rule-ref" href="livre_2.html#chap-6" data-tip="Entraînement : +1 palier chacun, plafond 3 (+3 déblocables à 2d6, max 6), 5 XP (10 si croisé) (§6.1).">entraînements</a>, FOR 5 = +2 <a class="rule-ref" href="livre_2.html#chap-6" data-tip="Niveau de dé : chaque cran monte le dé — d2 ➔ d4 ➔ d6 ➔ d8 ➔ d10 ➔ d12 (§6.1).">niveaux</a> → 4 niveaux depuis d2 (d2 ➔ d4 ➔ d6 ➔ d8 ➔ <strong>d10</strong>), 2 jetons cachés dans le poing.</em> Ne lance pas ton dé pour l'instant.<br>
                            <strong>2. Déclaration Masquée de la Cible :</strong> La cible déclare <strong>sans avoir vu ni le jet ni la mise de l'attaquant</strong> si elle engage une défense active ou si elle reste passive :
                            <ul>
                                <li><strong>Défense Active (Habituelle) :</strong> Le défenseur annonce sa compétence défensive (<em>Esquive</em> ou <em>Parade / Défense Corporelle</em>, avec malus d'1 <a class="rule-ref" href="livre_2.html#chap-6" data-tip="Niveau de dé : chaque cran monte le dé — d2 ➔ d4 ➔ d6 ➔ d8 ➔ d10 ➔ d12 (§6.1).">niveau de dé</a> si paré à l'arme sans spécialisation Épée) et engage sa mise : coût de base de réaction (1 PA) + PA bonus et/ou PE en jetons dans le poing (débités aussitôt à l'ouverture). <em>Exemple : Esquive 1 <a class="rule-ref" href="livre_2.html#chap-6" data-tip="Entraînement : +1 palier chacun, plafond 3 (+3 déblocables à 2d6, max 6), 5 XP (10 si croisé) (§6.1).">entraînement</a>, AGI 5 = +2 niveaux → 3 niveaux depuis d2 (d2 ➔ d4 ➔ d6 ➔ <strong>d8</strong>), 1 jeton caché dans le poing.</em></li>
                                <li><strong>Défense Passive (Choix Tactique) :</strong> Le défenseur <strong>décide de ne pas se défendre</strong> (pour préserver ses PA ou s'il est surpris). Il ne consomme aucun PA ni PE, ne lance aucun dé, son score de défense est égal à <strong>0</strong>, et l'attaque touche automatiquement avec la marge totale du jet attaquant !</li>
                            </ul> Tu veux réagir mais il te reste
                            0 PA ? Tu lances quand même ton dé de défense, normalement, avec une pénalité réflexe de -2. Pas de jetons possibles, juste ton dé à -2.<br>
                            <strong>3. Révélation Simultanée & Résolution Normale :</strong> Vous ouvrez les poings et lancez les deux dés <strong>ensemble</strong>, puis révélez tout en même temps. Le différentiel net
                            (Attaque finale − Défense finale) détermine la touche chirurgicale et les dégâts réels, exactement comme prévu (blocage si négatif, déviation si 0, touche + différentiel en dégâts si positif, puis armure, <a class="rule-ref" href="livre_6.html#sec-23-2" data-tip="Encaissement = CON × 2 : seuil comparé aux dégâts du coup (§23.2).">encaissement</a> et états).<br>
                            <strong>Variante sans jetons (déclaration ouverte) :</strong> Pas de jetons, pas de papier, pas envie ? L'attaquant annonce sa mise à voix haute et la paie aussitôt, puis le défenseur annonce la sienne en connaissance de cause. Vous lancez ensuite les deux dés ensemble. Zéro matériel, zéro triche possible. Revers : le défenseur ajuste en voyant ta mise.
                        </div>
                    </div>

                    <p id="sec-20-2-cibles">
                        <strong>Cible hors d'état de se défendre :</strong> ton adversaire ne peut pas
                        lancer de défense ? Pas de Face-à-Face. Applique son état :
                    </p>
                    <ul>
                        <li><strong>Inconscient :</strong> pas de jet. Tu infliges les dégâts maximum de ton attaque (dés au maximum + bonus). Ensuite, armure, <a class="rule-ref" href="livre_6.html#sec-23-2" data-tip="Encaissement = CON × 2 : seuil comparé aux dégâts du coup (§23.2).">encaissement</a> et arbre à 0 PV comme d'habitude (<a class="rule-ref" href="livre_6.html#sec-23-2" data-tip="Chute à 0 PV : Encaissement CON×2, Sombrer ou Dernier Souffle (§23.2).">§23.2</a>).</li>
                        <li><strong>Paralysé :</strong> fais ton jet d'attaque contre une <a class="rule-ref" href="#sec-20-2" data-tip="Défense Passive : le défenseur ne lance rien, score 0, touche auto avec ta marge (§20.2).">Défense Passive</a> imposée (score 0) : touche automatique avec ta marge totale.</li>
                        <li><strong>Sonné :</strong> il peut encore parer. Une fois à terre : −2 en défense (<a class="rule-ref" href="livre_6.html#sec-24-1" data-tip="À Terre : −1 ec en attaque, −2 en défense. Déplacement +1 PA/case (1 case = 2 PA). Se relever coûte 1 PA.">À Terre</a>). Debout, il pare normalement.</li>
                    </ul>

                    <p>
                        <strong>Règle d'or :</strong> quand un pouvoir, un sort ou une spécialisation contredit une règle de base, c'est lui qui gagne.
                    </p>
