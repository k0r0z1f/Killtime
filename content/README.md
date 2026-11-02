# Textes des Livres (Markdown)

Les `.md` ici sont la **source d'ecriture** des textes des livres.
Le JSON (`data/livre_N.json`) reste le format lu par le site : il est
**genere**, on ne l'edite plus a la main.

## Workflow

```sh
# 1. Editer un .md (texte + frontmatter : livre / cible / format)
# 2. Regenerer le JSON (les symboles "N." / "Chapitre N." sont recalcules
#    depuis l'ordre des sections : jamais de numero saisi a la main)
python3 tools/build_livres.py --build 1
# 3. Verifier la synchro (utile avant de tester en jeu / relire le site)
python3 tools/build_livres.py --check 1
# 4. Verifier liens + numerotation (ancres mortes, § sans chapitre,
#    § qui contredit son ancre, menus/badges incoherents)
python3 tools/check_liens.py
# 5. Menus lateraux : generes depuis les JSON (jamais edites a la main)
python3 tools/build_nav.py --build   # --check pour verifier
```

## Numerotation des chapitres (convention durable)

- Les chapitres sont numerotes en continu (1..N) dans l'ordre de lecture.
  `check_liens.py` refuse tout trou, doublon ou id non standard.
- **Inserer un chapitre = renumeroter** (jamais de suffixe type `7b`) :
  decaler les ids `chap-N` / `sec-N-` / symboles suivants, puis relancer
  les deux checks. Les renvois croises suivent via leurs ancres.
- Tout `§N` doit designer un chapitre existant et ne pas contredire
  l'ancre voisine : ecrire `(Livre X §N)` colle a un lien vers N.

## Formats

- `raw` : corps recopie octet pour octet dans le champ JSON.
  Pour le HTML complexe du site (callouts, `rule-ref`, badges) : coller le
  HTML brut, il passe tel quel. Pour les descriptions simples : ecrire du
  texte brut.
- `md` : Markdown converti en HTML au build (titres, gras, listes ;
  le HTML brut inline passe a travers).

## Perimetre

- `livre_1/especes/*.md` : les 6 descriptions d'especes (Livre I).
- `livre_N/sections/*/*.md` : tous les blocs HTML des sections (brut verbatim).
- Champs mecaniques (couts, seuils, dons, stats) : restent en JSON, pas de `.md`.
- Les blocs `ref` (cartes, tables, formules) ne sont pas des textes :
  ils restent generes depuis les donnees JSON.
