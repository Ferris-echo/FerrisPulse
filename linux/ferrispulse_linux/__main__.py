import sys

from . import cli


def main(argv=None):
    argv = list(sys.argv[1:] if argv is None else argv)
    if any(a in ("--cli", "--json") for a in argv):
        return cli.main(argv)
    # GUI 路径：把未知参数交给 GTK，但先处理 --hidden
    hidden = "--hidden" in argv
    argv = [a for a in argv if a != "--hidden"]
    from .app import FerrisPulseApp

    app = FerrisPulseApp(hidden=hidden)
    return app.run([sys.argv[0]] + argv)


if __name__ == "__main__":
    sys.exit(main() or 0)
