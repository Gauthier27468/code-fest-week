"""Point d'entrée du binaire gelé (PyInstaller).

En développement le bridge se lance avec `python -m kibird_bridge.bridge`, ce qui exécute le
module en gardant son paquet parent — les imports relatifs (`from . import protocol`) marchent.
PyInstaller, lui, veut un *script* : lui donner `kibird_bridge/bridge.py` directement le ferait
tourner comme module top-level `__main__`, hors paquet, et tous les imports relatifs casseraient.
D'où ce fichier, qui importe le paquet normalement puis appelle son `main()`.
"""
from kibird_bridge.bridge import main

if __name__ == "__main__":
    main()
