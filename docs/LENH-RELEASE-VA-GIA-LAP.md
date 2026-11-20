# Lệnh tạo release và gửi ảnh DICOM giả lập

Các lệnh dưới đây dành cho máy Linux hiện tại, source nằm tại `/home/duynguyen/Dev/DicomAgentPlugin`.

## 1. Tạo release mới

Sau khi cập nhật code, chạy:

```bash
bash /home/duynguyen/Dev/DicomAgentPlugin/deploy/release.sh
```

Script tự tăng patch version, restore → build → test, publish mới và tạo hai bộ cài Linux/Windows. Nếu có lỗi, script dừng; chỉ cập nhật checksum sau khi đóng gói và kiểm tra thành công.

Kết quả nằm tại:

```text
/home/duynguyen/Dev/DicomAgentPlugin/publish/installers/
├── dicom-agent_<version>_amd64.deb
├── DicomAgent-<version>-win-x64-Setup.exe
└── SHA256SUMS
```

Muốn chỉ định version, truyền một version lớn hơn version hiện tại, ví dụ:

```bash
bash /home/duynguyen/Dev/DicomAgentPlugin/deploy/release.sh 0.3.0
```

Giữ source ổn định trong lúc chạy release. Script tạo bộ cài; cài đè bộ cài mới để cập nhật ứng dụng đang dùng.

## 2. Giả lập một ảnh gửi vào app

### Chuẩn bị trong app

1. Kiểm tra dịch vụ đang `Running`.
2. Cấu hình DICOM để thử trên cùng máy:

   | Trường | Giá trị |
   |---|---|
   | AE Title | `DICOM_AGENT` |
   | IP lắng nghe | `127.0.0.1` hoặc `0.0.0.0` |
   | Port DICOM | `11112` |
   | Calling AE được phép | `XRAY_SIM` |
   | IP máy chụp được phép | `127.0.0.1` |

   Nếu đang dùng allowlist cho máy chụp thật, thêm `XRAY_SIM` và `127.0.0.1` vào danh sách hiện có, phân cách bằng dấu phẩy. Bấm **Lưu cấu hình** và xác thực quản trị nếu được yêu cầu; app tự áp dụng và kết nối lại.

3. Tạo/chọn một request thử nghiệm, bấm **Thực hiện / Nhận ảnh** trước khi gửi.

### Chạy trong Terminal

Simulator Linux đã được chuẩn bị tại đường dẫn bên dưới:

```bash
scu="/home/duynguyen/Dev/DicomAgentPlugin/publish/linux/scu/DicomAgent.Scu"

# Kiểm tra kết nối DICOM
"$scu" echo 127.0.0.1 11112 DICOM_AGENT XRAY_SIM

# Tạo đúng một ảnh DICOM giả lập trong thư mục tạm mới
shot_dir="$(mktemp -d)"
"$scu" generate "$shot_dir/test.dcm"

# Gửi ảnh vào app
"$scu" store 127.0.0.1 11112 DICOM_AGENT "$shot_dir/test.dcm" XRAY_SIM
```

Kết quả mong đợi:

```text
C-ECHO: 0000 (Success)
Synthetic DX fixture created.
C-STORE: 0000 (Success)
```

Quay lại màn hình **Thực hiện dịch vụ** để xem ảnh trong request đang nhận. Ảnh giả lập có PatientName `TEST^ONLY`, kích thước 2×2 pixel. File nguồn nằm trong thư mục tạm; bản nhận nằm trong thư mục lưu ảnh đã áp dụng của service.

Nếu C-STORE Success nhưng ảnh không xuất hiện trong request, kiểm tra đã bật nhận đúng ca trước khi gửi. Ảnh nhận khi chưa có ca đang nhận vẫn được lưu trong kho local.

Muốn dùng một file DICOM có sẵn, thay đường dẫn file trong lệnh store:

```bash
"$scu" store 127.0.0.1 11112 DICOM_AGENT "/duong-dan/anh.dcm" XRAY_SIM
```

### Khi simulator chưa có hoặc cần build lại

Chạy trên máy có .NET SDK:

```bash
dotnet publish /home/duynguyen/Dev/DicomAgentPlugin/tools/DicomAgent.Scu/DicomAgent.Scu.csproj \
  -c Release -r linux-x64 --self-contained true \
  -o /home/duynguyen/Dev/DicomAgentPlugin/publish/linux/scu
```

Port `11112` dùng nhận ảnh DICOM. Port `17890` dùng cho Web gọi API local; không dùng port này trong lệnh echo/store.
