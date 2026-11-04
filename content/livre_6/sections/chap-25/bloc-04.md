---
livre: 6
cible: sections.chap-25.blocs.4.html
format: raw
---


                    <h3 id="sec-25-2">25.2 L'Arbre de Résolution de la Chute à 0 PV</h3>
                    <p>
                        Lorsqu'une attaque réduit la vitalité d'un combattant à 0 PV ou moins, on compare les dégâts
                        réels subis lors de ce coup au Seuil d'Encaissement (<code>CON &times; 2</code>) :
                    </p>
                    <ul>
                        <li><strong>Branche 1 : Dégâts &ge; Seuil d'Encaissement (Traumatisme Incompressible) :</strong>
                            Aucun choix possible.
                            <ul>
                                <li><strong>Impact à la Tête / Crâne :</strong> <strong>Mort Instantanée</strong> par
                                    destruction cérébrale. Seule la dépense automatique d'un <a class="rule-ref" href="#sec-25-3" data-tip="Point de Miracle : dévie un coup mortel, survie à 1 PV sous le seuil létal. Rachat : 20 XP ou jalon MJ.">Point de Miracle</a>
                                    permet de survivre in extremis à 1 PV (coma profond).</li>
                                <li><strong>Impact au Cœur / Cou :</strong> Coma immédiat et agonie rapide (hémorragie
                                    mortelle en <code>CON / 2</code> tours).</li>
                                <li><strong>Torse &amp; Membres :</strong> Syncope forcée (état <a class="rule-ref" href="#sec-26-5" data-tip="Inconscient : aucune action ni réaction. Réveil par secousse vigoureuse (1 PA) ou premiers soins.">Inconscient</a> et
                                    <a class="rule-ref" href="#sec-26-1" data-tip="À Terre : −1 ec en attaque, −2 en défense. Déplacement +1 PA/case (1 case = 2 PA). Se relever coûte des PA.">À Terre</a> immédiat).
                                </li>
                            </ul>
                        </li>
                        <li><strong>Branche 2 : Dégâts &lt; Seuil d'Encaissement (Choc Contrôlé / Épuisement) :</strong>
                            Le corps s'éteint par pure décharge vitale. Le combattant choisit :
                            <ul>
                                <li><strong>Sombrer (La Syncope Protectrice) :</strong> Tu t'évanouis
                                    et tu restes stable au sol (<a class="rule-ref" href="#sec-26-5" data-tip="Inconscient : aucune action ni réaction. Réveil par secousse vigoureuse (1 PA) ou premiers soins.">Inconscient</a>, <a class="rule-ref" href="#sec-26-1" data-tip="À Terre : −1 ec en attaque, −2 en défense. Déplacement +1 PA/case (1 case = 2 PA). Se relever coûte des PA.">À Terre</a>).
                                    En général, tes adversaires te laissent là où tu es tombé et passent à une
                                    autre cible : tu ne bouges plus, tu ne les menaces plus. Exemple : tu t'effondres
                                    au milieu d'une fusillade, le tireur en face réoriente son arme vers ton
                                    allié debout. Mais ce n'est pas automatique : une bête affamée, un tueur
                                    méthodique ou un ennemi qui veut s'assurer du résultat peut quand même
                                    t'achever. Et là, tu es sans défense : il doit encore réussir son attaque,
                                    mais tu ne peux ni parer ni esquiver. Si son coup te touche, tu meurs.
                                    Exemple : le tueur s'accroupit près de toi et vise ta tête — un seul coup
                                    suffit. Ton MJ tranche selon la situation.</li>
                                <li><strong>Dernier Souffle (Le Baroud d'Honneur) :</strong> Tu refuses de
                                    t'évanouir. Tu restes conscient, au sol (<a class="rule-ref" href="#sec-26-1" data-tip="À Terre : −1 ec en attaque, −2 en défense. Déplacement +1 PA/case (1 case = 2 PA). Se relever coûte des PA.">À Terre</a> et
                                    <a class="rule-ref" href="#sec-26-1" data-tip="Agonisant : aucune action raisonnée, fuite de 6 cases (1d8). Persiste jusqu’à stabilisation médicale.">Agonisant</a>). Tu gardes tes PA et tu peux encore agir. Mais chaque
                                    épreuve que tu tentes te coûte <strong>+1 Essoufflement direct</strong> : dès
                                    que tu lances les dés — épreuve de compétence ou face à face — ton souffle
                                    monte d'un cran. Les gestes simples sans jet (ramper d'une case, parler,
                                    lâcher un objet) ne te coûtent rien. Si ton Essoufflement atteint ta
                                    Constitution, ton cœur lâche pour de bon.
                                    Exemple : tu lances ta corde à ton allié pour le tirer à couvert — c'est une
                                    épreuve, +1 Essoufflement. Ramper d'une case juste après, sans jet, ne coûte
                                    rien. Mais au troisième lancer de dés, ton souffle rejoint ta Constitution :
                                    arrêt cardiaque définitif.
                                </li>
                            </ul>
                        </li>
                    </ul>

                    <h3 id="sec-25-3">25.3 Le Point de Miracle : L'Ultime Rempart contre la Mort</h3>
                    <p>
                        Chaque Personnage Joueur (PJ) commence sa carrière avec <strong>1 Point de Miracle</strong>
                        inscrit sur sa fiche de personnage.
                    </p>
                    <div class="callout warning">
                        <div class="callout-header">
                            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                                stroke-width="2">
                                <circle cx="12" cy="12" r="10" />
                                <line x1="12" y1="8" x2="12" y2="12" />
                                <line x1="12" y1="16" x2="12.01" y2="16" />
                            </svg>
                            Usage Vital du Point de Miracle
                        </div>
                        <div class="callout-body">
                            Lorsqu'un PJ subit un coup mortel ou atteint ou dépasse son seuil létal (<span
                                class="hl-ruby">Constitution &times; 5</span>), le joueur peut dépenser son Point de
                            Miracle. Le coup est providentiellement dévié (la balle ricoche sur un briquet en métal, la
                            lame glisse sur une boucle de ceinture, un éclat de nytharite absorbe le rayon). Le PJ
                            échappe à la mort immédiate et reste vivant à <strong>1 point en dessous de son seuil
                                létal</strong>, mais sombre dans l'inconscience.<br><br>
                            <em>Régénération :</em> Un Point de Miracle ne se régénère jamais par simple repos. Il ne
                            peut être racheté qu'avec <strong>20 points d'XP</strong> ou accordé par le Maître de Jeu
                            lors de l'accomplissement d'un jalon narratif transcendant.
                        </div>
                    </div>

                    <h3>25.4 Résistances & Vulnérabilités Raciales</h3>
                    <p>
                        Certains dons raciaux (Livre I §2.2) confèrent une <strong>résistance</strong> ou une
                        <strong>vulnérabilité</strong> à un élément (feu, froid, toxines, gaz, etc.). Sans cette
                        mention explicite, aucune espèce n'est résistante ni vulnérable : les dégâts directs sont
                        réduits par l'armure puis la Constitution (§25.1), sans distinction d'élément.
                    </p>
                    <div class="callout-body">
                        &bull; <strong>Résistance (élément) :</strong> les dégâts directs de cet élément sont réduits
                        du montant indiqué, <strong>après l'armure et avant la Constitution</strong>. Les dégâts
                        résiduels du même élément démarrent à -1 (minimum 1).<br>
                        &bull; <strong>Vulnérabilité (élément) :</strong> +1 dégât subi de cet élément (appliqué
                        après réductions) ; -2 aux épreuves de Constitution contre cet élément, sauf mention
                        contraire.<br>
                        &bull; <strong>Non-cumul :</strong> un personnage ne choisit qu'un seul don racial à la
                        création ; si deux sources accordent une résistance au même élément, seule la plus forte
                        s'applique.
                    </div>

                    <!-- ================= CHAPITRE 29 ================= -->
                    
