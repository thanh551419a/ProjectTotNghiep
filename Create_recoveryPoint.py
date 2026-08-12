
import pandas as pd
import numpy as np
from pathlib import Path


# ============================================================
# CONFIG
# ============================================================

# Folder chứa các file playerStart_rally_*.csv
INPUT_FOLDER = Path(
    r"Y:\Learn\ProjectTotNghiep\AI\rallyDataset"
)

# File output
OUTPUT_FILE = Path(
    r"Y:\Learn\ProjectTotNghiep\AI\recovery_dataset.csv"
)

# Chỉ lấy 1/3 cuối của đoạn sau Serve
LAST_PART_RATIO = 1 / 3

# Ngưỡng R² để xem trajectory gần tuyến tính
LINEAR_R2_THRESHOLD = 0.90

# Số lần đổi hướng tối đa để vẫn xem là tuyến tính
DIRECTION_CHANGE_THRESHOLD = 2


# ============================================================
# EVENT HELPER
# ============================================================

def get_event(value):
    """
    Chuẩn hóa event.

    None / NaN / chuỗi rỗng / "None"
    đều được xem là không có event.
    """

    if pd.isna(value):
        return None

    value = str(value).strip()

    if value == "":
        return None

    if value.lower() == "none":
        return None

    return value


# ============================================================
# EXTRACT SEGMENT SAU PLAYER SERVE
# ============================================================

def extract_post_serve_segment(df):
    """
    Segment bắt đầu tại:

        PlayerEvent = Serve

    Sau đó tiếp tục lấy dữ liệu cho đến khi:

        BotEvent = một event khác:
            - None
            - MoveLeft
            - MoveRight

    Các trường hợp:

        BotEvent = None
            -> tiếp tục

        BotEvent = MoveLeft
            -> tiếp tục

        BotEvent = MoveRight
            -> tiếp tục

        BotEvent = Bump / Set / Spike / Serve / ...
            -> kết thúc segment

    Nếu không xuất hiện event kết thúc:
        -> lấy đến cuối file.

    PlayerEvent sau Serve KHÔNG dùng để kết thúc segment.
    """

    serve_index = None

    # --------------------------------------------------------
    # Tìm Player Serve
    # --------------------------------------------------------

    for i in range(len(df)):

        player_event = get_event(
            df.iloc[i]["PlayerEvent"]
        )

        if player_event == "Serve":

            serve_index = i
            break

    # Không có Serve
    if serve_index is None:
        return None

    segment_rows = []

    # --------------------------------------------------------
    # Lấy dữ liệu từ Serve
    # --------------------------------------------------------

    for i in range(
        serve_index,
        len(df)
    ):

        row = df.iloc[i]

        segment_rows.append(row)

        # Không kiểm tra event kết thúc tại chính frame Serve
        if i == serve_index:
            continue

        bot_event = get_event(
            row["BotEvent"]
        )

        # ----------------------------------------------------
        # Bot đang di chuyển hoặc chưa có event
        # -> tiếp tục lấy dữ liệu
        # ----------------------------------------------------

        if bot_event in (
            None,
            "MoveLeft",
            "MoveRight"
        ):

            continue

        # ----------------------------------------------------
        # Bot thực hiện event khác
        # -> kết thúc segment
        # ----------------------------------------------------

        break

    # Cần ít nhất Serve + 1 dòng dữ liệu
    if len(segment_rows) < 2:
        return None

    return pd.DataFrame(
        segment_rows,
        columns=df.columns
    )


# ============================================================
# ĐẾM SỐ LẦN PLAYER ĐỔI HƯỚNG
# ============================================================

def count_direction_changes(segment):
    """
    MoveLeft  = -1
    MoveRight = +1

    Đếm số lần Player đổi hướng.
    """

    directions = []

    for event in segment["PlayerEvent"]:

        event = get_event(event)

        if event == "MoveLeft":

            directions.append(-1)

        elif event == "MoveRight":

            directions.append(1)

    if len(directions) < 2:
        return 0

    changes = 0

    for i in range(
        1,
        len(directions)
    ):

        if directions[i] != directions[i - 1]:

            changes += 1

    return changes


# ============================================================
# TÍNH R² CỦA TRAJECTORY X
# ============================================================

def calculate_r2(x_values):
    """
    Fit:

        X = a * time + b

    Sau đó tính R².

    R² gần 1:
        trajectory gần tuyến tính.

    R² thấp:
        trajectory thay đổi / gấp khúc nhiều hơn.
    """

    if len(x_values) < 2:
        return 0.0

    y = np.asarray(
        x_values,
        dtype=float
    )

    time = np.arange(
        len(y),
        dtype=float
    )

    # Tất cả X giống nhau
    if np.allclose(
        y,
        y[0]
    ):

        return 1.0

    # Linear regression
    coefficients = np.polyfit(
        time,
        y,
        1
    )

    slope = coefficients[0]
    intercept = coefficients[1]

    predicted = (
        slope * time
        + intercept
    )

    ss_res = np.sum(
        (y - predicted) ** 2
    )

    ss_tot = np.sum(
        (y - np.mean(y)) ** 2
    )

    if ss_tot == 0:
        return 1.0

    r2 = 1 - (
        ss_res / ss_tot
    )

    return float(r2)


# ============================================================
# TÍNH RECOVERY X
# ============================================================

def calculate_recovery_x(segment):
    """
    Chỉ dùng 1/3 cuối của segment.

    Nếu trajectory gần tuyến tính:
        RecoveryX = Mean

    Nếu trajectory gấp khúc / đổi hướng:
        RecoveryX = Median
    """

    # --------------------------------------------------------
    # Lấy PlayerPosX
    # --------------------------------------------------------

    x_series = pd.to_numeric(
        segment["PlayerPosX"],
        errors="coerce"
    )

    x_series = x_series.dropna()

    x_values = x_series.to_numpy()

    if len(x_values) < 2:
        return None

    # --------------------------------------------------------
    # Lấy 1/3 cuối
    # --------------------------------------------------------

    last_count = max(
        1,
        int(
            np.ceil(
                len(x_values)
                * LAST_PART_RATIO
            )
        )
    )

    last_third = x_values[
        -last_count:
    ]

    # --------------------------------------------------------
    # Phân tích toàn bộ trajectory
    # --------------------------------------------------------

    r2 = calculate_r2(
        x_values
    )

    direction_changes = (
        count_direction_changes(
            segment
        )
    )

    # --------------------------------------------------------
    # Quyết định Mean hay Median
    # --------------------------------------------------------

    if (
        r2 >= LINEAR_R2_THRESHOLD
        and
        direction_changes
        < DIRECTION_CHANGE_THRESHOLD
    ):

        # Gần tuyến tính
        recovery_x = float(
            np.mean(
                last_third
            )
        )

        method = "Mean_Last_Third"

    else:

        # Có xu hướng gấp khúc / đổi hướng
        recovery_x = float(
            np.median(
                last_third
            )
        )

        method = "Median_Last_Third"

    return {
        "recovery_x": recovery_x,
        "method": method,
        "r2": r2,
        "direction_changes": direction_changes
    }


# ============================================================
# MAIN
# ============================================================

def main():

    # --------------------------------------------------------
    # Tìm toàn bộ playerStart_rally
    # --------------------------------------------------------

    rally_files = sorted(
        INPUT_FOLDER.glob(
            "playerStart_rally_*.csv"
        )
    )

    print("=" * 70)
    print("PLAYER RECOVERY DATASET")
    print("=" * 70)

    print(
        f"Input folder : {INPUT_FOLDER}"
    )

    print(
        f"Found rallies: {len(rally_files)}"
    )

    print()

    if len(rally_files) == 0:

        print(
            "Không tìm thấy file "
            "playerStart_rally_*.csv"
        )

        return

    results = []

    # ========================================================
    # XỬ LÝ TỪNG RALLY
    # ========================================================

    for index, rally_file in enumerate(
        rally_files,
        start=1
    ):

        print(
            f"[{index}/{len(rally_files)}] "
            f"{rally_file.name}"
        )

        # ----------------------------------------------------
        # Đọc CSV
        # ----------------------------------------------------

        try:

            df = pd.read_csv(
                rally_file
            )

        except Exception as e:

            print(
                f"    ERROR đọc file: {e}"
            )

            continue

        # ----------------------------------------------------
        # Kiểm tra column
        # ----------------------------------------------------

        required_columns = {
            "PlayerPosX",
            "PlayerEvent",
            "BotEvent"
        }

        missing_columns = (
            required_columns
            - set(df.columns)
        )

        if missing_columns:

            print(
                "    SKIP - thiếu column: "
                f"{missing_columns}"
            )

            continue

        # ====================================================
        # EXTRACT SEGMENT
        # ====================================================

        segment = (
            extract_post_serve_segment(
                df
            )
        )

        if segment is None:

            print(
                "    SKIP - không tạo được segment"
            )

            continue

        # ====================================================
        # TÍNH RECOVERY X
        # ====================================================

        recovery = (
            calculate_recovery_x(
                segment
            )
        )

        if recovery is None:

            print(
                "    SKIP - không đủ PlayerPosX"
            )

            continue

        # ====================================================
        # SỐ DÒNG DỮ LIỆU
        # ====================================================

        data_row_count = len(
            segment
        )

        # ====================================================
        # TRỌNG SỐ
        #
        # Số dòng càng nhiều
        # -> trọng số càng cao
        # ====================================================

        weight = data_row_count

        # ====================================================
        # LƯU KẾT QUẢ
        # ====================================================

        results.append({

            "file_name":
                rally_file.name,

            "recovery_x":
                recovery["recovery_x"],

            "recovery_method":
                recovery["method"],

            "data_row_count":
                data_row_count,

            "weight":
                weight,

            "r2":
                recovery["r2"],

            "direction_changes":
                recovery[
                    "direction_changes"
                ]

        })

        print(
            f"    RecoveryX : "
            f"{recovery['recovery_x']:.2f}"
        )

        print(
            f"    Method    : "
            f"{recovery['method']}"
        )

        print(
            f"    Rows      : "
            f"{data_row_count}"
        )

        print(
            f"    Weight    : "
            f"{weight}"
        )

    # ========================================================
    # KHÔNG CÓ KẾT QUẢ
    # ========================================================

    if not results:

        print()
        print(
            "Không có rally hợp lệ."
        )

        return

    # ========================================================
    # DATAFRAME KẾT QUẢ
    # ========================================================

    result_df = pd.DataFrame(
        results
    )

    # ========================================================
    # TÍNH RECOVERY X CHUNG
    #
    # Weighted Mean:
    #
    # Σ(RecoveryX × Weight)
    # ---------------------
    #       Σ Weight
    # ========================================================

    weighted_sum = (
        result_df["recovery_x"]
        *
        result_df["weight"]
    ).sum()

    total_weight = (
        result_df["weight"]
    ).sum()

    weighted_recovery_x = (
        weighted_sum
        /
        total_weight
    )

    # ========================================================
    # GHI FILE CSV
    # ========================================================

    OUTPUT_FILE.parent.mkdir(
        parents=True,
        exist_ok=True
    )

    result_df.to_csv(
        OUTPUT_FILE,
        index=False,
        encoding="utf-8-sig"
    )

    # ========================================================
    # SUMMARY
    # ========================================================

    print()
    print("=" * 70)
    print("RESULT")
    print("=" * 70)

    print(
        f"Valid rallies       : "
        f"{len(result_df)}"
    )

    print(
        f"Total data rows     : "
        f"{result_df['data_row_count'].sum()}"
    )

    print(
        f"Total weight        : "
        f"{total_weight}"
    )

    print(
        f"Weighted RecoveryX  : "
        f"{weighted_recovery_x:.2f}"
    )

    print()
    print(
        f"Output file:"
    )

    print(
        OUTPUT_FILE
    )

    print()
    print("=" * 70)
    print("DONE")
    print("=" * 70)


# ============================================================
# ENTRY POINT
# ============================================================

if __name__ == "__main__":
    main()
