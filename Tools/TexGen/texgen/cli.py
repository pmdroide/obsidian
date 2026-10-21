"""Command line: generate / check / styles. stdout is JSON lines only; exit codes in Docs/Research/AiTextures/PLAN.md."""
import argparse
import sys
import traceback

from . import PIPELINE_VERSION, __version__, pipeline, protocol
from .backends import BACKENDS, BackendUnavailable, check_backend
from .styles import style as style_module

EXIT_OK, EXIT_INTERNAL, EXIT_ARGS, EXIT_MODEL, EXIT_SEAM, EXIT_STRICT = 0, 1, 2, 3, 4, 5


class _Parser(argparse.ArgumentParser):
    def error(self, message):
        protocol.error("invalid_arguments", message)
        sys.exit(EXIT_ARGS)


def _build_parser():
    parser = _Parser(prog="texgen", description="Local PBR texture generator for Obsidian.")
    parser.add_argument("--version", action="store_true", help="print the version and exit")
    sub = parser.add_subparsers(dest="command")

    gen = sub.add_parser("generate", help="generate a PBR texture set")
    gen.add_argument("--prompt", required=True)
    gen.add_argument("--seed", type=int, required=True)
    gen.add_argument("--out", required=True, help="output folder (written atomically via OUT.tmp)")
    gen.add_argument("--negative-prompt", default="")
    gen.add_argument("--resolution", type=int, default=1024)
    gen.add_argument("--backend", choices=BACKENDS, default="fake")
    gen.add_argument("--style", default="source2")
    gen.add_argument("--family", default="auto")
    gen.add_argument("--input-rgb", default=None)
    gen.add_argument("--force-dielectric", action="store_true")
    gen.add_argument("--normal-convention", choices=("directx", "opengl"), default="directx")
    gen.add_argument("--no-seam-check", action="store_true")
    gen.add_argument("--strict", action="store_true", help="exit 5 when a style rule is broken")
    gen.add_argument("--pipeline-version", default=PIPELINE_VERSION)

    chk = sub.add_parser("check", help="check that a backend can run")
    chk.add_argument("--backend", choices=BACKENDS, default="fake")

    sub.add_parser("styles", help="list style profiles and families as JSON")
    return parser


def _generate(args):
    size = args.resolution
    if size < 16 or size > 8192 or size & (size - 1):
        protocol.error("invalid_arguments", "--resolution must be a power of two between 16 and 8192")
        return EXIT_ARGS
    if not args.prompt.strip():
        protocol.error("invalid_arguments", "--prompt must not be empty")
        return EXIT_ARGS
    params = {
        "prompt": args.prompt.strip(), "negative_prompt": args.negative_prompt.strip(), "seed": args.seed,
        "resolution": size, "backend": args.backend, "style": args.style, "family": args.family,
        "force_dielectric": args.force_dielectric, "input_rgb": args.input_rgb,
        "normal_convention": args.normal_convention, "seam_check": not args.no_seam_check,
        "strict": args.strict, "pipeline_version": args.pipeline_version,
    }
    try:
        out = pipeline.run(params, args.out)
    except pipeline.TexGenError as e:
        protocol.error(e.code, e.message)
        return e.exit_code
    except BackendUnavailable as e:
        protocol.error(e.code, e.message)
        return EXIT_MODEL
    protocol.done(out=str(out).replace("\\", "/"))
    return EXIT_OK


def _check(args):
    ok, code, message = check_backend(args.backend)
    if ok:
        protocol.done(backend=args.backend, message=message)
        return EXIT_OK
    protocol.error(code, message)
    return EXIT_MODEL


def _styles(_args):
    protocol.done(styles=style_module.describe_styles())
    return EXIT_OK


def main(argv=None):
    parser = _build_parser()
    args = parser.parse_args(argv)
    if args.version:
        print(__version__)
        return EXIT_OK
    if args.command is None:
        parser.error("a command is required: generate, check or styles")
    try:
        return {"generate": _generate, "check": _check, "styles": _styles}[args.command](args)
    except Exception as e:  # noqa: last resort, keeps stdout protocol-clean
        protocol.log(traceback.format_exc())
        protocol.error("internal_error", "%s: %s" % (type(e).__name__, e))
        return EXIT_INTERNAL
