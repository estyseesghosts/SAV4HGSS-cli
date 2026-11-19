"""Summarise an `aux_fault_matrix.py` run into one table (FMT-06).

Reads `leveld-out/aux-matrix/report.json` and states, per variant, what the
decomp says should happen (Save_CheckFrontierData, save.c:534-561) and what the
game did: the message shown, which partition each aux copy came from and what
the repair pass wrote back into SAVE_MISC.

  python tools/aux_matrix_report.py leveld-out/aux-matrix --out research/aux-matrix.json
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

MAGIC = 0x20060623
AUX_SIZE = 0x0BB0        # battle hall, SAV4HGSS.cs:43
AUX_IDX = 1
AUX_PAYLOAD = AUX_SIZE - 0x10

# The strings the game can print for these paths (check_savedata.c:258-283).
MESSAGES = {
    "err1=2": "The most recent Battle Hall record is corrupted.",
    "err1=3": "The Battle Hall record has been erased.",
}


def copy_ok(c: dict) -> bool:
    return (c["magic"] == MAGIC and c["size"] == AUX_PAYLOAD
            and c["idx"] == AUX_IDX and c["crc_ok"])


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("run")
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    report = json.loads((Path(args.run) / "report.json").read_text(encoding="utf-8"))
    rows = []
    for r in report["rows"]:
        after = r.get("after_save", {})
        aux_out = after.get("aux", {})
        misc_out = after.get("misc", {})
        rows.append({
            "variant": r["variant"],
            "predicted": r["predicted"],
            "aux_in": {p: {"valid": copy_ok(v["copy"]), "saveno": v["copy"]["saveno"]}
                       for p, v in r["aux_in"].items()},
            "reached_field": r.get("reached_field"),
            "loaded_partition": r.get("loaded"),
            "written_partition": r.get("written"),
            "aux_after": {p: {"valid": copy_ok(v["copy"]), "saveno": v["copy"]["saveno"],
                              "idx": v["copy"]["idx"]}
                          for p, v in aux_out.items()},
            "misc_after": {p: {"extraChunksExist": v["extraChunksExist"],
                               "prefer": v["prefer"]}
                           for p, v in misc_out.items()},
            "screenshots": [s["file"] for s in r["screenshots"]],
        })

    evidence = {
        "schema": "hgss-save.aux-matrix/v1",
        "question": "FMT-06",
        "base": report["base"],
        "chunk": "battle-hall (id 1, partition-relative 0x26000, 0x0BB0 bytes)",
        "rules": {
            "gate": "Save_CheckExtraChunksExist(SAVE_MISC + 0x29B bit 0) must be set; "
                    "otherwise Save_CheckFrontierData returns without looking at any chunk "
                    "(save.c:540)",
            "key": "chunk 1-5 are skipped when both stored keys are -1 (save.c:542)",
            "copies": "copy 1 is the chunk at `sector` (partition A), copy 2 at `sector+64` "
                      "(partition B); flash is 128 sectors of 0x1000 (save.c:906-907)",
            "selection": "chunk 1-5 ignore saveno. The prefer byte (SAVE_MISC + 0x2D0[id-1]) "
                         "selects which key is tested first; only falling through to the "
                         "second key sets ret2_p, which becomes statusFlags |= 4 "
                         "(save.c:1057-1078, save.c:546-547)",
            "erased": "no valid copy -> ret_p = 2 -> err = 3 -> statusFlags |= 8 "
                      "(save.c:1087-1095, save.c:544-545)",
        },
        "messages": MESSAGES,
        "rows": rows,
    }
    Path(args.out).write_text(json.dumps(evidence, indent=2), encoding="utf-8")

    print(f"{'variant':<24} {'in p0/p1':<12} {'loaded':<7} {'prefer after':<14} observed")
    for r in rows:
        inp = "/".join("ok" if r["aux_in"][p]["valid"] else "BAD" for p in ("p0", "p1"))
        pref = "%d,%d" % (r["misc_after"]["p0"]["prefer"][0],
                          r["misc_after"]["p1"]["prefer"][0]) if r["misc_after"] else "-"
        print(f"{r['variant']:<24} {inp:<12} {str(r['loaded_partition']):<7} {pref:<14} {r['predicted']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
