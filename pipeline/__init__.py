"""Emotion-conveying interior appearance via LLM -- generation pipeline.

Implements design-spec.md: frozen discrete pools, constrained LLM selection,
validation before anything reaches a participant, and the two control arms.
"""

__all__ = [
    "controls",
    "generate",
    "pools",
    "prompts",
    "schema",
    "session",
    "validate",
]
