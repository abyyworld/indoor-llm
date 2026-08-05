"""Join one participant's separate output files into a single record.

The study writes five files per participant, at four different grains: one row per
trial, one row per review trial, one row per discrete event, one row per 20 Hz sample,
and one row per consent action. That split is right at write time -- a single writer
would couple the affect grid to the telemetry clock -- but it is the wrong shape for
analysis, and it is the wrong shape for an archive. Six months from now the question
"what did p07 actually do" should have one answer, not five files to re-join by hand
against a memory of how they lined up.

So this produces one long-format file: every row from every source, in timestamp order,
with a `source` column saying where it came from and every column any source contributed.
Nothing is dropped, aggregated or rounded -- a bundle that lost information would be
worse than the files it replaces, because it would look complete.
"""

from __future__ import annotations

import csv
import io
import json
from pathlib import Path
from typing import Any


SOURCES = {
    "responses.csv": "trial",
    "oversight_responses.csv": "review",
    "consent_log.csv": "consent",
}

# Prefixes of the per-run files, which carry a participant and timestamp in the name.
RUN_SOURCES = {"_events.csv": "event", "telemetry_": "telemetry"}


class BundleError(Exception):
    pass


def _read(path: Path) -> list[dict[str, str]]:
    with path.open(newline="", encoding="utf-8") as handle:
        return list(csv.DictReader(handle))


def _sort_key(row: dict[str, str]) -> tuple[int, float, int]:
    """Order by wall-clock where a row has one, else by session clock.

    Rows without any timestamp sort first rather than being dropped: a row with no time
    is still evidence, and silently discarding it is how a bundle starts lying.
    """
    for field in ("utc_ms", "unix_ms"):
        value = row.get(field)
        if value:
            try:
                return (1, float(value) / 1000.0, 0)
            except ValueError:
                pass
    for field in ("utc", "started_utc", "timestamp_utc"):
        value = row.get(field)
        if value:
            return (2, 0.0, 0)
    value = row.get("t_session")
    if value:
        try:
            return (3, float(value), 0)
        except ValueError:
            pass
    return (0, 0.0, 0)


def collect(data_dir: Path, participant: str) -> list[dict[str, str]]:
    """Every row belonging to `participant`, tagged with its source."""
    rows: list[dict[str, str]] = []

    for name, source in SOURCES.items():
        path = data_dir / name
        if not path.exists():
            continue
        for row in _read(path):
            if row.get("participant") not in (participant, None, ""):
                continue
            if row.get("participant") != participant:
                continue
            rows.append({"source": source, **row})

    logs = data_dir / "logs"
    for directory in (data_dir, logs):
        if not directory.is_dir():
            continue
        for path in sorted(directory.iterdir()):
            if not path.is_file() or path.suffix != ".csv":
                continue
            for marker, source in RUN_SOURCES.items():
                if marker not in path.name:
                    continue
                if participant not in path.name:
                    continue
                for row in _read(path):
                    rows.append({"source": source, "source_file": path.name, **row})

    rows.sort(key=_sort_key)
    return rows


def to_csv(rows: list[dict[str, str]]) -> str:
    """Long-format CSV: the union of every column any source contributed."""
    if not rows:
        raise BundleError("no rows found for that participant")

    columns: list[str] = ["source", "source_file"]
    for row in rows:
        for key in row:
            if key not in columns:
                columns.append(key)

    out = io.StringIO()
    writer = csv.DictWriter(out, fieldnames=columns, extrasaction="ignore")
    writer.writeheader()
    for row in rows:
        writer.writerow(row)
    return out.getvalue()


def summarise(rows: list[dict[str, str]], participant: str) -> dict[str, Any]:
    counts: dict[str, int] = {}
    for row in rows:
        counts[row["source"]] = counts.get(row["source"], 0) + 1

    withdrew = any(
        row["source"] == "consent" and row.get("event") == "withdrawn" for row in rows
    )
    consented = any(
        row["source"] == "consent" and row.get("event") == "consent_taken" for row in rows
    )
    return {
        "participant": participant,
        "rows": len(rows),
        "by_source": counts,
        "consent_recorded": consented,
        "withdrew": withdrew,
        "trials": counts.get("trial", 0),
        "review_trials": counts.get("review", 0),
        # Stated rather than inferred. A bundle whose completeness has to be guessed at
        # is one somebody will guess wrong about.
        "complete": consented and not withdrew and counts.get("trial", 0) >= 8,
    }


def bundle(data_dir: Path, participant: str, out_dir: Path) -> dict[str, Any]:
    rows = collect(data_dir, participant)
    if not rows:
        raise BundleError(
            f"no data for {participant!r} in {data_dir}. Check the participant id and "
            f"that the session actually wrote files."
        )

    out_dir.mkdir(parents=True, exist_ok=True)
    csv_path = out_dir / f"{participant}_all.csv"
    csv_path.write_text(to_csv(rows), encoding="utf-8")

    report = summarise(rows, participant)
    report["file"] = str(csv_path)
    (out_dir / f"{participant}_summary.json").write_text(
        json.dumps(report, indent=2) + "\n", encoding="utf-8"
    )
    return report
