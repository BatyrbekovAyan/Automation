#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""B1 «расписание бота» — the schedule's window predicate, in ONE place (spec
docs/superpowers/specs/2026-10-14-paying-users-protection-and-schedule-design.md §4 B1, §1).

By default a bot has no schedule. Inside its window it works as usual; OUTSIDE the window it behaves exactly as in
«Вместе» (no auto-reply, suggestions work, history + memory written by Record Suppressed, no quota spent, the event not
claimed). The decision is taken at message entry — it is one more OR term of the templates' Read Reply Mode
(apply-history-handoff.py READ_MODE_SQL composes OFF_HOURS_SQL below) and Acquire Send Lease never reads it. The
schedule outranks a chat's explicit «Авто», like the handoff pause. No row, or a disabled one, changes nothing.

The row (table public.bot_schedule, sql/2026-10-14-bot-schedule.sql): days_mask bit 0 = Monday … bit 6 = Sunday;
start_min / end_min = minutes of the bot's local day (tz, IANA; the bot's {bot}TimeZone, Asia/Almaty by default):
  start == end  → the whole day, every enabled day;
  start <  end  → [start, end) of an enabled day;
  start >  end  → across midnight: [start, 1440) ∪ [0, end), and the day that decides is the day the window STARTED —
                  for the after-midnight half (m < end) the PREVIOUS calendar day of the bot's zone.
Start inclusive, end exclusive; seconds do not count (08:59:59 is minute 539).

WINDOW_SQL(now_expr) takes the clock as a placeholder: the canon passes CANON_CLOCK (`now() at time zone s.tz` — the
bot's wall time now), tests a fixed instant. The same cases (Tools/n8n/fixtures/schedule-cases.json — name, tz, nowIso
(a UTC instant), daysMask, startMin, endMin, enabled, expectOpen) feed the client's twin (ScheduleModel.IsInsideWindow)
and tests/test_reply_schedule.py. expectOpen reads «the schedule lets the bot answer»: a disabled case is open whatever
its window, so expectOpen == (not enabled) or «the window holds nowIso in tz»; a disabled case also stands for a bot with
NO row (the server tests run every disabled case both ways — `no_row_reads_as_disabled` names that point).

    python3 Tools/n8n/bot_schedule.py              # print the canonical off-hours term
"""

CANON_CLOCK = "now() at time zone s.tz"     # the bot's wall time now (timestamp without time zone, in the row's zone)


def WINDOW_SQL(now_expr):
    """A boolean SQL expression «the window of the bot_schedule row `s` is open at `now_expr`» — `now_expr` is a
    timestamp WITHOUT time zone already in the bot's local wall time (the canon: CANON_CLOCK). It reads s.days_mask,
    s.start_min and s.end_min only; s.enabled is the caller's (a disabled schedule never closes anything)."""
    t = "(" + now_expr + ")"
    m = "(extract(hour from " + t + ")::int * 60 + extract(minute from " + t + ")::int)"
    # the day the window started: the previous calendar day for the after-midnight half of a window across midnight
    start_day = ("(" + t + " - case when s.start_min > s.end_min and " + m + " < s.end_min"
                 " then interval '1 day' else interval '0 days' end)")
    day_on = "(((s.days_mask >> (extract(isodow from " + start_day + ")::int - 1)) & 1) = 1)"
    return ("(" + day_on + "\n"
            "      and (s.start_min = s.end_min\n"
            "        or (s.start_min < s.end_min and " + m + " >= s.start_min and " + m + " < s.end_min)\n"
            "        or (s.start_min > s.end_min and (" + m + " >= s.start_min or " + m + " < s.end_min))))")


def OFF_HOURS_SQL(now_expr=CANON_CLOCK, profile="$1"):
    """The mode read's term: true only while the profile has an ENABLED schedule whose window is closed; no row or a
    disabled one → false (absence still means «reply»). `profile` is the profile id's SQL expression."""
    return ("coalesce((select not " + WINDOW_SQL(now_expr) + "\n"
            "    from bot_schedule s where s.profile_id = " + profile + " and s.enabled), false)")


if __name__ == "__main__":
    print(OFF_HOURS_SQL())
