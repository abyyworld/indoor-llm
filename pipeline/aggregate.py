"""Collapsing N samples per cell into the one config that gets built.

PROPOSAL, not a decision. See proposals-for-review.md section 4. Mengkai owns the
aggregation method; this exists so the choice can be tried on real samples rather than
argued about in the abstract, and so nothing downstream is blocked while it is open.

The constraint this has to satisfy is hers, from 31 Jul, and it is the right one:

    "whatever combination reaches you will always be one the model actually produced
     as a single coherent output, not something reconstructed by combining the
     'winning' value from each variable independently"

That rules out the obvious approach. Taking the modal hue and the modal illuminance
separately can produce a pairing no sample ever generated -- a saturated warm hue landing
against a low illuminance that was never chosen alongside it. `modal_reconstruction`
below implements that wrong approach anyway, because being able to show how often it
differs from the medoid is itself worth reporting.

Medoid selection instead: keep the single real sample that sits closest to the middle of
all the others. The output is a genuine sample by construction, so her constraint holds
without needing to be checked.
"""

#: NOT the method of record as of 8 Aug 2026.
#:
#: configs/pools.json declares `mode_then_medoid`, which is Mengkai's own script
#: (`Sample_and_aggregate.py`) -- a plurality vote on brightness_lux, then a categorical
#: medoid on the rest within that subset. The stimuli the study actually runs on came
#: from that, imported via `pipeline.cli import-handoff`.
#:
#: Plain medoid stays here and stays correct. It is what `make-study-config.py` produces,
#: and its role now is the sensitivity check: if both methods pick the same rooms, the
#: choice of summary did not matter and the write-up can say so in a sentence.

from __future__ import annotations

from collections import Counter
from typing import Any, Callable, Iterable, Sequence

# These cover BOTH vocabularies on purpose: the names this repo currently uses
# (hue, saturation, brightness, texture) and the names Mengkai's template uses
# (hue_category, saturation_pct, brightness_lux, material, roughness). A config may
# arrive in either form depending on whether it came from this pipeline or from her
# handover file, and a field missing from these lists is silently ignored by every
# distance calculation, which reads as "these two rooms are identical" rather than as
# an error. That is exactly how a separability check can pass while comparing nothing.

#: Variables compared by equality. Distance 0 if equal, 1 if not.
CATEGORICAL: tuple[str, ...] = (
    "hue_category", "material", "material_type", "roughness", "achromatic",
    "texture",
)

#: Variables compared by normalised absolute difference.
CONTINUOUS: tuple[str, ...] = (
    "saturation_pct", "brightness_lux", "hue", "saturation",
    "brightness",
)


class AggregationError(ValueError):
    pass


def _numeric(value: Any) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool)


def _ranges(samples: Sequence[dict], fields: Iterable[str]) -> dict[str, float]:
    """Span of each continuous field, used to normalise so no field dominates.

    A lux range of 35 to 950 and a saturation of 20 to 40 are not comparable raw: without
    normalising, illuminance would decide the medoid on its own.
    """
    spans: dict[str, float] = {}
    for field in fields:
        values = [s[field] for s in samples if field in s and _numeric(s[field])]
        if len(values) < 2:
            continue
        span = max(values) - min(values)
        if span > 0:
            spans[field] = float(span)
    return spans


def distance(a: dict, b: dict, spans: dict[str, float], weights: dict[str, float] | None = None) -> float:
    """Mixed categorical and continuous distance between two samples.

    Gower-style: each field contributes between 0 and 1, and the result is the mean over
    the fields both samples actually carry. Weights are left open on purpose -- whether
    illuminance should count as much as hue is a research judgement, not mine.
    """
    weights = weights or {}
    total = 0.0
    used = 0.0

    for field in CATEGORICAL:
        if field in a and field in b:
            w = weights.get(field, 1.0)
            total += w * (0.0 if a[field] == b[field] else 1.0)
            used += w

    for field in CONTINUOUS:
        if field in a and field in b and _numeric(a[field]) and _numeric(b[field]):
            span = spans.get(field)
            if span is None:
                continue
            w = weights.get(field, 1.0)
            total += w * (abs(a[field] - b[field]) / span)
            used += w

    if used == 0:
        raise AggregationError("two samples share no comparable field; check field names")
    return total / used


def medoid(
    samples: Sequence[dict],
    weights: dict[str, float] | None = None,
) -> tuple[dict, dict]:
    """Return (the chosen sample, statistics about the choice).

    The chosen sample is a real one, never a synthesised combination. Ties break by the
    earliest index, so the result is deterministic and reproducible.
    """
    if not samples:
        raise AggregationError("no samples to aggregate")
    if len(samples) == 1:
        return samples[0], {
            "n_samples": 1,
            "aggregation": "medoid",
            "mean_distance": 0.0,
            "consistency": None,
            "mode_share": {},
            "note": "single sample, nothing to aggregate",
        }

    spans = _ranges(samples, CONTINUOUS)

    totals: list[float] = []
    for i, candidate in enumerate(samples):
        total = sum(
            distance(candidate, other, spans, weights)
            for j, other in enumerate(samples)
            if i != j
        )
        totals.append(total)

    best = min(range(len(samples)), key=lambda i: (totals[i], i))
    mean_distance = totals[best] / (len(samples) - 1)

    return samples[best], {
        "n_samples": len(samples),
        "aggregation": "medoid",
        "mean_distance": round(mean_distance, 4),
        # 1.0 means every sample identical, 0.0 maximally spread. Reported rather than
        # thresholded: what counts as "consistent enough" is Mengkai's call.
        "consistency": round(1.0 - mean_distance, 4),
        "mode_share": mode_share(samples),
        "selected_index": best,
    }


def mode_share(samples: Sequence[dict]) -> dict[str, float]:
    """Per-variable agreement, for thesis section 3.3.

    Reported alongside the medoid rather than used to choose it. A cell where hue agrees
    100% but material splits 50/50 is a different situation from one where both are at
    75%, and the medoid alone would hide that.
    """
    out: dict[str, float] = {}
    for field in CATEGORICAL:
        values = [s[field] for s in samples if field in s]
        if values:
            out[field] = round(Counter(values).most_common(1)[0][1] / len(values), 4)
    return out


def modal_reconstruction(samples: Sequence[dict]) -> dict:
    """The approach Mengkai's constraint rules out. Here to measure, not to use.

    Takes the modal value of each variable independently. How often this differs from the
    medoid is worth reporting: if they always agree, the constraint costs nothing; if they
    often disagree, it was load-bearing and that is a finding.
    """
    if not samples:
        raise AggregationError("no samples to aggregate")

    out: dict[str, Any] = {}
    for field in CATEGORICAL:
        values = [s[field] for s in samples if field in s]
        if values:
            out[field] = Counter(values).most_common(1)[0][0]
    for field in CONTINUOUS:
        values = [s[field] for s in samples if field in s and _numeric(s[field])]
        if values:
            out[field] = sorted(values)[len(values) // 2]  # median
    return out


def differs_from_modal(samples: Sequence[dict], weights: dict[str, float] | None = None) -> bool:
    """True when the medoid and the ruled-out modal reconstruction disagree."""
    chosen, _ = medoid(samples, weights)
    modal = modal_reconstruction(samples)
    for field in CATEGORICAL:
        if field in chosen and field in modal and chosen[field] != modal[field]:
            return True
    return False


def aggregate_cells(
    samples_by_cell: dict[tuple, Sequence[dict]],
    weights: dict[str, float] | None = None,
    on_cell: Callable[[tuple, dict, dict], None] | None = None,
) -> dict[tuple, dict]:
    """Aggregate every (emotion, shape) cell, carrying the statistics through."""
    out: dict[tuple, dict] = {}
    for cell, samples in samples_by_cell.items():
        chosen, stats = medoid(samples, weights)
        record = dict(chosen)
        record.update(
            {
                "target_emotion": cell[0],
                "shape": cell[1],
                "n_samples": stats["n_samples"],
                "aggregation": stats["aggregation"],
                "consistency": stats["consistency"],
            }
        )
        out[cell] = record
        if on_cell is not None:
            on_cell(cell, record, stats)
    return out
