import os
import glob
import pandas as pd

# Absolute path
log_folder = r"Y:\Learn\ProjectTotNghiep\MyGame\Logs"

# Lấy tất cả file csv
csv_files = glob.glob(os.path.join(log_folder, "*.csv"))

if not csv_files:
    print("Không tìm thấy file CSV nào.")
    exit()

# Lấy file mới nhất theo thời gian sửa đổi
latest_file = max(csv_files, key=os.path.getmtime)

print("Latest file:", latest_file)

# Đọc CSV
df = pd.read_csv(latest_file)

# Hiển thị thông tin
print(df.head())

# Nếu muốn xem toàn bộ cột
print(df.columns.tolist())