"""Makes `import texgen` work when the tests are run from the repository root (discover Tools/TexGen/tests)."""
import sys
from pathlib import Path

TEXGEN_ROOT = Path(__file__).resolve().parent.parent
if str(TEXGEN_ROOT) not in sys.path:
    sys.path.insert(0, str(TEXGEN_ROOT))
