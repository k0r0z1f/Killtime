# Textes des Livres (Markdown)

Les `.md` ici sont la **source d'ecriture** des textes des livres.
Le JSON (`data/livre_N.json`) reste le format lu par le site : il est
**genere**, on ne l'edite plus a la main.

## Workflow

```sh
# 1. Editer un .md (texte + frontmatter : livre / cible / format)
# 2. Regenerer le JSON
python3 tools/build_livres.py --build 1
# 3. Verifier la synchro (utile avant de tester en jeu / relire le site)
python3 tools/build_livres.py --check 1
```

## Formats

- `raw` : corps recopie octet pour octet dans le champ JSON.
  Pour le HTML complexe du site (callouts, `rule-ref`, badges) : coller le
  HTML brut, il passe tel quel. Pour les descriptions simples : ecrire du
  texte brut.
- `md` : Markdown converti en HTML au build (titres, gras, listes ;
  le HTML brut inline passe a travers).

## Perimetre pilote (Livre I)

- `livre_1/especes/*.md` : les 6 descriptions d'especes.
- `livre_1/sections/*/*.md` : tous les blocs HTML des 6 sections (brut verbatim).
- Champs mecaniques (couts, seuils, dons, stats) : restent en JSON, pas de `.md`.
- Les blocs `ref` (cartes, tables, formules) ne sont pas des textes :
  ils restent generes depuis les donnees JSON.
