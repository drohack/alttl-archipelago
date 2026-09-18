import os

from test.bases import WorldTestBase


class ALTTLTestBase(WorldTestBase):
    game = "A Little to the Left"


def fixture_path(name: str) -> str:
    """A file in the repo's fixtures/ directory.

    fixtures/, deliberately NOT apworld/alttl/data/. Everything under the world
    package is packaged into the shipped .apworld, and a test fixture has no
    business in a player's install.

    Found by walking up to the repo root, because the world is imported from a
    checkout here but from a zip in production, where __file__ has no usable
    parent on disk.
    """
    here = os.path.dirname(os.path.abspath(__file__))
    while here != os.path.dirname(here):
        candidate = os.path.join(here, "fixtures")
        if os.path.isdir(candidate):
            return os.path.join(candidate, name)
        here = os.path.dirname(here)
    raise RuntimeError("could not locate fixtures/ from " + __file__)
