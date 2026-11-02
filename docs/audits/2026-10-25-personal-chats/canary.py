#!/usr/bin/env python3
"""«Личные и рабочие чаты», пакет выкладки, шаг 9.5 — текст canary.sql с отметкой шага 9.0 (офлайн, ни одного запроса).

Шаг 9.0 сохраняет ответ canary-baseline.sql в canary-baseline.txt — вывод sql-oneoff.py, одна строка
`200 {"baseline_at": "…Z", "memory_max_id": "…"}`. Этот скрипт берёт оттуда отметку, проверяет её вид (момент — время UTC
вида 2026-11-02T10:00:00.123456Z, id — целое число) и печатает canary.sql с ней; текст выполняет sql-oneoff.py. canary.sql
судит только строки, записанные после отметки, поэтому без отметки его не выполнить: без подстановки он падает на приведении
типа. Ответ не 200, не тот вид, нет файла — ничего не печатается, код 1: canary не выполнять, сначала шаг 9.0.

  canary.py <canary-baseline.txt>           # печатает SQL; README: SQL="$(python3 canary.py …)" && python3 sql-oneoff.py "$SQL"
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
CANARY_SQL = os.path.join(HERE, "canary.sql")
ANSWER = re.compile(r"^200 (\{.*\})$")
BASELINE_AT = re.compile(r"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,6})?Z$")
MEMORY_MAX_ID = re.compile(r"^\d{1,18}$")
# placeholder in canary.sql → (the answer's key, the form its value must have)
FIELDS = {"__BASELINE_AT__": ("baseline_at", BASELINE_AT), "__MEMORY_MAX_ID__": ("memory_max_id", MEMORY_MAX_ID)}


def fail(why):
    print(f"canary.py: {why} — canary НЕ выполнять: сначала шаг 9.0 (отметка), затем этот шаг", file=sys.stderr)
    sys.exit(1)


def baseline(path):
    """{placeholder: value} from the 9.0 answer, or fail() — never a value that is not exactly the expected form."""
    if not os.path.exists(path):
        fail(f"нет файла отметки {path}")
    lines = [line.strip() for line in open(path, encoding="utf-8").read().splitlines() if line.strip()]
    answers = [m.group(1) for m in (ANSWER.match(line) for line in lines) if m]
    if len(answers) != 1:
        fail(f"в {path} нет ровно одной строки ответа `200 {{…}}` (строк ответа: {len(answers)})")
    try:
        body = json.loads(answers[0])
    except ValueError:
        fail("ответ отметки — не JSON")
    if not isinstance(body, dict) or set(body) != {key for key, _ in FIELDS.values()}:
        fail(f"в ответе отметки не те поля: {sorted(body) if isinstance(body, dict) else type(body).__name__}")
    values = {}
    for placeholder, (key, form) in FIELDS.items():
        value = body[key]
        if not isinstance(value, str) or not form.match(value):
            fail(f"{key} не того вида: {value!r}")
        values[placeholder] = value
    return values


def main():
    if len(sys.argv) != 2:
        fail("нужен один аргумент — путь к canary-baseline.txt")
    values = baseline(sys.argv[1])
    sql = open(CANARY_SQL, encoding="utf-8").read()
    for placeholder, value in values.items():
        if sql.count(placeholder) != 1:
            fail(f"в canary.sql метка {placeholder} встречается {sql.count(placeholder)} раз (ждём ровно один)")
        sql = sql.replace(placeholder, value)
    sys.stdout.write(sql)


if __name__ == "__main__":
    main()
