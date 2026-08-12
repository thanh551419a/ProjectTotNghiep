import pandas as pd
from pathlib import Path


# ============================================================
# CONFIG
# ============================================================

INPUT_FOLDER = Path(
    r"Y:\Learn\ProjectTotNghiep\AI\NormalizedRallyDataSet"
)

OUTPUT_FOLDER = Path(
    r"Y:\Learn\ProjectTotNghiep\AI\FinalRallyDataSet"
)

PLAYER_EVENT = "PlayerEvent"
BOT_EVENT = "BotEvent"


# ============================================================
# KIỂM TRA EVENT RỖNG
# ============================================================

def is_empty(value):

    if pd.isna(value):
        return True

    value = str(value).strip()

    return (
        value == ""
        or value.lower() == "none"
        or value.lower() == "nan"
    )


# ============================================================
# MAIN
# ============================================================

def main():

    OUTPUT_FOLDER.mkdir(
        parents=True,
        exist_ok=True
    )

    rally_files = sorted(
        INPUT_FOLDER.glob("*.csv")
    )

    print(
        f"Found {len(rally_files)} rally files."
    )

    kept_count = 0
    removed_count = 0

    # ========================================================
    # DUYỆT TOÀN BỘ RALLY
    # ========================================================

    for rally_file in rally_files:

        try:

            df = pd.read_csv(rally_file)

        except Exception as e:

            print(
                f"ERROR reading {rally_file.name}: {e}"
            )

            continue

        # ----------------------------------------------------
        # Kiểm tra column
        # ----------------------------------------------------

        if (
            PLAYER_EVENT not in df.columns
            or BOT_EVENT not in df.columns
        ):

            print(
                f"SKIP {rally_file.name}: "
                f"missing event columns"
            )

            continue

        # ====================================================
        # KIỂM TRA CÁC EVENT
        # ====================================================

        has_non_serve_event = False

        for _, row in df.iterrows():

            player_event = row[PLAYER_EVENT]
            bot_event = row[BOT_EVENT]

            # -----------------------------------------------
            # PlayerEvent
            # -----------------------------------------------

            if not is_empty(player_event):

                if str(player_event).strip().lower() != "serve":

                    has_non_serve_event = True
                    break

            # -----------------------------------------------
            # BotEvent
            # -----------------------------------------------

            if not is_empty(bot_event):

                if str(bot_event).strip().lower() != "serve":

                    has_non_serve_event = True
                    break

        # ====================================================
        # CHỈ CÓ SERVE
        # ====================================================

        if not has_non_serve_event:

            removed_count += 1

            print(
                f"REMOVE: {rally_file.name} "
                f"(Serve only)"
            )

            continue

        # ====================================================
        # CÓ EVENT KHÁC SERVE
        # ====================================================

        output_file = (
            OUTPUT_FOLDER
            / rally_file.name
        )

        df.to_csv(
            output_file,
            index=False,
            encoding="utf-8-sig"
        )

        kept_count += 1

        print(
            f"KEEP: {rally_file.name}"
        )

    # ========================================================
    # SUMMARY
    # ========================================================

    print()
    print("=" * 70)
    print("FILTER SUMMARY")
    print("=" * 70)

    print(
        f"Input rallies  : {len(rally_files)}"
    )

    print(
        f"Kept rallies   : {kept_count}"
    )

    print(
        f"Removed rallies: {removed_count}"
    )

    print()
    print(
        f"Output folder: {OUTPUT_FOLDER}"
    )

    print()
    print("DONE")


if __name__ == "__main__":
    main()