import pandas as pd
from pathlib import Path


# ============================================================
# CONFIG
# ============================================================

INPUT_FOLDER = Path(
    r"Y:\Learn\ProjectTotNghiep\AI\RallyDataSet"
)

OUTPUT_FOLDER = Path(
    r"Y:\Learn\ProjectTotNghiep\AI\NormalizedRallyDataSet"
)


PLAYER_EVENT = "PlayerEvent"
BOT_EVENT = "BotEvent"


# ============================================================
# KIỂM TRA EVENT CÓ RỖNG KHÔNG
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
# CHUẨN HÓA 1 FILE RALLY
# ============================================================

def normalize_rally(df):

    rows = []

    removed_rows = 0
    normalized_rows = 0

    for _, row in df.iterrows():

        player_empty = is_empty(
            row[PLAYER_EVENT]
        )

        bot_empty = is_empty(
            row[BOT_EVENT]
        )

        # ====================================================
        # CẢ HAI ĐỀU RỖNG
        #
        # => XÓA FRAME
        # ====================================================

        if player_empty and bot_empty:

            removed_rows += 1

            continue

        # ====================================================
        # PLAYER CÓ EVENT
        # BOT RỖNG
        #
        # => Giữ PlayerEvent
        # => BotEvent = None
        # ====================================================

        if not player_empty and bot_empty:

            row[BOT_EVENT] = "None"

            normalized_rows += 1

        # ====================================================
        # PLAYER RỖNG
        # BOT CÓ EVENT
        #
        # => PlayerEvent = None
        # => Giữ BotEvent
        # ====================================================

        elif player_empty and not bot_empty:

            row[PLAYER_EVENT] = "None"

            normalized_rows += 1

        # ====================================================
        # CẢ HAI CÓ EVENT
        #
        # => GIỮ NGUYÊN
        # ====================================================

        else:

            pass

        rows.append(row)

    return (
        pd.DataFrame(
            rows,
            columns=df.columns
        ),
        removed_rows,
        normalized_rows
    )


# ============================================================
# MAIN
# ============================================================

def main():

    # --------------------------------------------------------
    # Tạo folder output
    # --------------------------------------------------------

    OUTPUT_FOLDER.mkdir(
        parents=True,
        exist_ok=True
    )

    # --------------------------------------------------------
    # Lấy toàn bộ Rally CSV
    # --------------------------------------------------------

    rally_files = sorted(
        INPUT_FOLDER.glob("*.csv")
    )

    print(
        f"Found {len(rally_files)} rally files."
    )

    # ========================================================
    # THỐNG KÊ TOÀN DATASET
    # ========================================================

    total_input_rows = 0
    total_output_rows = 0
    total_removed_rows = 0
    total_normalized_rows = 0

    # ========================================================
    # DUYỆT TOÀN BỘ RALLY
    # ========================================================

    for rally_file in rally_files:

        print()
        print("=" * 70)
        print(
            f"Processing: {rally_file.name}"
        )

        try:

            df = pd.read_csv(rally_file)

        except Exception as e:

            print(
                f"ERROR reading file: {e}"
            )

            continue

        # ----------------------------------------------------
        # Kiểm tra column
        # ----------------------------------------------------

        if (
            PLAYER_EVENT not in df.columns
            or
            BOT_EVENT not in df.columns
        ):

            print(
                "Missing PlayerEvent or BotEvent"
            )

            continue

        # ----------------------------------------------------
        # Chuẩn hóa
        # ----------------------------------------------------

        (
            normalized_df,
            removed_rows,
            normalized_rows
        ) = normalize_rally(df)

        # ----------------------------------------------------
        # Ghi output
        # ----------------------------------------------------

        output_file = (
            OUTPUT_FOLDER
            / rally_file.name
        )

        normalized_df.to_csv(
            output_file,
            index=False,
            encoding="utf-8-sig"
        )

        # ----------------------------------------------------
        # Thống kê
        # ----------------------------------------------------

        input_rows = len(df)
        output_rows = len(normalized_df)

        total_input_rows += input_rows
        total_output_rows += output_rows
        total_removed_rows += removed_rows
        total_normalized_rows += normalized_rows

        print(
            f"Input rows      : {input_rows}"
        )

        print(
            f"Removed rows    : {removed_rows}"
        )

        print(
            f"Output rows     : {output_rows}"
        )

        print(
            f"Normalized rows : {normalized_rows}"
        )

    # ========================================================
    # TỔNG KẾT
    # ========================================================

    print()
    print("=" * 70)
    print("NORMALIZATION SUMMARY")
    print("=" * 70)

    print(
        f"Rally files       : {len(rally_files)}"
    )

    print(
        f"Input rows        : {total_input_rows}"
    )

    print(
        f"Removed rows      : {total_removed_rows}"
    )

    print(
        f"Output rows       : {total_output_rows}"
    )

    print(
        f"Normalized rows   : {total_normalized_rows}"
    )

    print()
    print(
        f"Output folder:"
    )

    print(
        OUTPUT_FOLDER
    )

    print()
    print("DONE")


# ============================================================
# RUN
# ============================================================

if __name__ == "__main__":
    main()