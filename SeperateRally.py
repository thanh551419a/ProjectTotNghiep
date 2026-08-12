import pandas as pd
from pathlib import Path


# ============================================================
# CONFIG
# ============================================================

INPUT_FOLDER = Path(
    r"Y:\Learn\ProjectTotNghiep\AI\DataSet"
)

OUTPUT_FOLDER = Path(
    r"Y:\Learn\ProjectTotNghiep\AI\RallyDataSet"
)


# ============================================================
# GET EVENT
# ============================================================

def get_event(value):

    if pd.isna(value):
        return ""

    return str(value).strip()


# ============================================================
# XÁC ĐỊNH SERVE
# ============================================================

def get_serve_type(row):

    player_event = get_event(row["PlayerEvent"])
    bot_event = get_event(row["BotEvent"])

    if player_event == "Serve":
        return "player"

    if bot_event == "Serve":
        return "bot"

    return None


# ============================================================
# MAIN
# ============================================================

def main():

    # --------------------------------------------------------
    # Tạo duy nhất một folder output
    # --------------------------------------------------------

    OUTPUT_FOLDER.mkdir(
        parents=True,
        exist_ok=True
    )

    # --------------------------------------------------------
    # Xóa các file rally cũ nếu muốn chạy lại dataset
    # --------------------------------------------------------

    for old_file in OUTPUT_FOLDER.glob("*.csv"):
        old_file.unlink()

    # --------------------------------------------------------
    # Lấy toàn bộ file CSV
    # --------------------------------------------------------

    csv_files = sorted(
        INPUT_FOLDER.glob("*.csv")
    )

    print(
        f"Found {len(csv_files)} match files."
    )

    # ========================================================
    # COUNTER TOÀN BỘ DATASET
    # ========================================================

    player_rally_count = 0
    bot_rally_count = 0

    total_rallies = 0

    # ========================================================
    # DUYỆT TỪNG FILE
    # ========================================================

    for csv_file in csv_files:

        print()
        print("=" * 70)
        print(f"Processing: {csv_file.name}")

        try:

            df = pd.read_csv(csv_file)

        except Exception as e:

            print(
                f"ERROR: {e}"
            )

            continue

        # ----------------------------------------------------
        # Tìm tất cả Serve trong file
        # ----------------------------------------------------

        serve_points = []

        for index, row in df.iterrows():

            serve_type = get_serve_type(row)

            if serve_type is not None:

                serve_points.append(
                    (index, serve_type)
                )

        print(
            f"Found {len(serve_points)} serves"
        )

        # ====================================================
        # TÁCH RALLY
        # ====================================================

        for i, (start_index, serve_type) in enumerate(
            serve_points
        ):

            # ------------------------------------------------
            # Rally kết thúc ở dòng trước Serve tiếp theo
            # ------------------------------------------------

            if i + 1 < len(serve_points):

                next_start_index = (
                    serve_points[i + 1][0]
                )

                end_index = next_start_index - 1

            else:

                # Rally cuối cùng của file
                end_index = df.index[-1]

            # ------------------------------------------------
            # Giữ nguyên toàn bộ bảng dữ liệu
            # ------------------------------------------------

            rally = df.loc[
                start_index:end_index
            ].copy()

            if rally.empty:
                continue

            # =================================================
            # PLAYER START
            # =================================================

            if serve_type == "player":

                player_rally_count += 1

                file_name = (
                    f"playerStart_rally_"
                    f"{player_rally_count}.csv"
                )

            # =================================================
            # BOT START
            # =================================================

            else:

                bot_rally_count += 1

                file_name = (
                    f"botStart_rally_"
                    f"{bot_rally_count}.csv"
                )

            # ------------------------------------------------
            # Ghi vào MỘT folder duy nhất
            # ------------------------------------------------

            output_file = (
                OUTPUT_FOLDER / file_name
            )

            rally.to_csv(
                output_file,
                index=False,
                encoding="utf-8-sig"
            )

            total_rallies += 1

            print(
                f"  {file_name} "
                f"| Frame {rally.iloc[0]['Frame']}"
                f" -> {rally.iloc[-1]['Frame']}"
                f" | {len(rally)} frames"
            )

    # ========================================================
    # THỐNG KÊ TOÀN BỘ DATASET
    # ========================================================

    print()
    print("=" * 70)
    print("DATASET SUMMARY")
    print("=" * 70)

    print(
        f"Input match files       : {len(csv_files)}"
    )

    print(
        f"Player-start rallies    : "
        f"{player_rally_count}"
    )

    print(
        f"Bot-start rallies       : "
        f"{bot_rally_count}"
    )

    print(
        f"Total rallies           : "
        f"{total_rallies}"
    )

    print()
    print(
        f"Output folder: {OUTPUT_FOLDER}"
    )

    print()
    print("DONE")


if __name__ == "__main__":
    main()