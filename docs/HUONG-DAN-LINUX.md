# Cài đặt và giả lập máy X-quang trên Linux

Tài liệu dùng gói Ubuntu/Debian amd64, systemd, libc >= 2.34. Host chạy nền; Desktop cần phiên đồ họa. Nếu cần cài trên distro khác, phải xác nhận tương thích gói và thư viện native trước.

## 1. Chuẩn bị bộ cài

Lấy `publish/installers/dicom-agent_0.1.0_amd64.deb` từ máy build và copy sang máy Linux đích. Gói self-contained không cần .NET Runtime; `apt` vẫn cần nguồn package/cache để cài đủ dependency hệ điều hành.

Nếu chưa có gói, chạy tại root repo trên máy build có SDK theo `global.json`:

```bash
bash deploy/linux/publish.sh
bash deploy/linux/build-deb.sh 0.1.0
```

Không cài song song bằng `.deb` và `deploy/linux/install.sh`; unit cài tay có thể ghi đè unit của package.

## 2. Cài đặt

Trên máy Linux đích, trong thư mục chứa gói:

```bash
sudo apt install ./dicom-agent_0.1.0_amd64.deb
sudo usermod -aG dicom-agent-operators "$USER"
```

Lệnh thêm group chạy từ terminal của người vận hành; nếu đang dùng root, thay `"$USER"` bằng tên tài khoản desktop cần cấp quyền. Đăng xuất rồi đăng nhập lại, sau đó:

```bash
id
systemctl status dicom-agent.service --no-pager
dicom-agent-desktop
```

Group phải có `dicom-agent-operators`; service phải `active (running)`. Nếu chưa chạy:

```bash
sudo systemctl start dicom-agent.service
```

| Thành phần | Vị trí mặc định |
|---|---|
| Host/Desktop | `/opt/dicom-agent/host`, `/opt/dicom-agent/desktop` |
| Dữ liệu/cấu hình/DB | `/var/lib/dicom-agent` |
| Ảnh | `/var/lib/dicom-agent/storage` |
| Service user | `dicom-agent` |

Không chạy thêm Host thủ công khi service đã dùng cùng port/data directory. Có thể đóng Desktop mà service vẫn nhận ảnh.

## 3. Chuẩn bị SCU giả lập

Tại root repo trên máy build Linux có .NET SDK:

```bash
dotnet publish tools/DicomAgent.Scu -c Release -r linux-x64 --self-contained true -o publish/linux/scu
```

Copy toàn bộ thư mục `publish/linux/scu` đến máy đích, ví dụ `~/DicomSimulator`. Build này dành cho Linux x64 tương thích, không phải ARM64. Nếu copy làm mất quyền thực thi, khôi phục:

```bash
cd ~/DicomSimulator
chmod u+x DicomAgent.Scu
```

SCU không có sẵn trong `.deb`. Nếu máy đang dùng chính là máy build, có thể chạy trực tiếp trong `publish/linux/scu` thay cho `~/DicomSimulator` ở các bước sau.

## 4. Cấu hình thử nghiệm loopback

Mở ứng dụng → **Refresh → Settings**:

| Trường | Giá trị |
|---|---|
| AE Title | `DICOM_AGENT` |
| Listen IP address | `127.0.0.1` |
| Port | `11112` |
| Allowed Calling AE Titles | `XRAY_SIM` |
| Allowed source IPs | `127.0.0.1` |
| Storage directory | Để trống |
| Maximum concurrent associations | `10` |
| Maximum instance size (bytes) | `268435456` |
| Enable receiver / Enable file history | Bật cả hai |

Bấm **Validate → Save settings**, sau đó:

```bash
sudo systemctl restart dicom-agent.service
systemctl status dicom-agent.service --no-pager
ss -ltn 'sport = :11112'
```

Trở lại Desktop → **Refresh**, xác nhận `Running`, đúng AE/port. Loopback không cần mở firewall cho máy khác. Không cấu hình `DICOM_AGENT_DATA` tùy ý khi mở Desktop để tránh trỏ sai socket của service đã cài.

## 5. Kiểm tra kết nối DICOM

```bash
cd ~/DicomSimulator
./DicomAgent.Scu echo 127.0.0.1 11112 DICOM_AGENT XRAY_SIM
```

Mong đợi: `C-ECHO: 0000 (Success)`. Simulator mở kết nối khi chạy lệnh; UI chưa có danh sách thiết bị luôn kết nối.

## 6. Giả lập chụp và gửi ảnh

Chạy trong cùng terminal:

```bash
shot_dir="$(mktemp -d "$HOME/dicom-shot-XXXXXX")"
shot_file="$shot_dir/shot.dcm"
./DicomAgent.Scu generate "$shot_file"
./DicomAgent.Scu store 127.0.0.1 11112 DICOM_AGENT "$shot_file" XRAY_SIM
```

Mong đợi: tạo fixture thành công và `C-STORE: 0000 (Success)`. Mỗi lần generate tạo UID mới; công cụ từ chối ghi đè file nguồn có sẵn.

Ảnh DX tổng hợp chỉ 2×2 pixel, PatientID `SYNTHETIC`, PatientName `TEST^ONLY`. Bước này mô phỏng dữ liệu sau chụp, không điều khiển phần cứng/phát tia hay đánh giá chất lượng ảnh lâm sàng.

## 7. Xác nhận lưu ảnh và chống trùng

Trong Desktop: **Refresh → Received files → chọn ảnh → Open file location**.

Đường dẫn đích mặc định:

```text
/var/lib/dicom-agent/storage/YYYY-MM-DD/StudyUID/SeriesUID/SOPUID.dcm
```

Ngày/thời gian là UTC. File trong `shot_dir` là nguồn; file dưới storage là bản Agent nhận và lưu. Có thể liệt kê file đích:

```bash
find /var/lib/dicom-agent/storage -type f -name '*.dcm'
```

Gửi lại cùng file trong terminal đang giữ biến `shot_file`:

```bash
./DicomAgent.Scu store 127.0.0.1 11112 DICOM_AGENT "$shot_file" XRAY_SIM
```

Mong đợi Success nhưng không thêm file/bản ghi cho cùng ảnh. Chạy lại bước 6 để tạo ảnh mới. Đóng Desktop, gửi một ảnh mới rồi mở lại/Refresh để kiểm tra Host hoạt động độc lập.

Agent chưa có viewer hoặc chuyển JPEG/PNG. Mở `.dcm` bằng viewer DICOM riêng nếu cần; upload website bằng thao tác chọn file thủ công.

## 8. Thử gửi từ máy khác

1. Lấy IP LAN máy Agent bằng `ip -br address`, ví dụ `192.168.1.10`.
2. Settings: Listen IP `0.0.0.0`; Allowed source IPs là IP thật của máy gửi, ví dụ `192.168.1.20`; Calling AE `XRAY_SIM`.
3. Save, restart service. Nếu tiếp tục thử local, thêm `127.0.0.1` vào allowlist, cách nhau bằng dấu phẩy.
4. Nếu UFW đang được sử dụng, mở đúng nguồn:

```bash
sudo ufw status verbose
sudo ufw allow from 192.168.1.20 to any port 11112 proto tcp
```

Rule chỉ có hiệu lực khi UFW hoạt động. Không bật UFW mù quáng trên máy quản trị từ xa; quản trị mạng cần giữ đường SSH và áp dụng firewall thực tế của hệ thống.

Trên máy gửi Linux có SCU, thay IP ví dụ bằng IP Agent:

```bash
./DicomAgent.Scu echo 192.168.1.10 11112 DICOM_AGENT XRAY_SIM
./DicomAgent.Scu store 192.168.1.10 11112 DICOM_AGENT /duong-dan/anh-thu.dcm XRAY_SIM
```

Đổi `/duong-dan/anh-thu.dcm` thành file tổng hợp đã generate. Với máy ảo, cấu hình Bridged/Host-only hoặc đường truy cập NAT phù hợp; allowlist phải khớp IP nguồn Agent thực sự nhìn thấy.

## 9. Chẩn đoán

```bash
journalctl -u dicom-agent.service -n 100 --no-pager
systemctl status dicom-agent.service --no-pager
ss -ltn 'sport = :11112'
```

| Hiện tượng | Kiểm tra |
|---|---|
| UI không mở | Phiên đồ họa, native dependencies; không mở UI bằng root để thay thế việc cấp group |
| UI Disconnected | Service, group sau đăng nhập lại, `DICOM_AGENT_DATA` và quyền socket |
| Timeout/refused | Port, listen IP, firewall, mạng máy ảo |
| Association rejected | Called/Calling AE, IP nguồn allowlist, restart sau Save |
| Save custom storage thất bại | Quyền service và sandbox systemd; xem hướng dẫn Prod |
| File có nhưng history thiếu | Lỗi DB/quyền/dung lượng; chờ reconciliation 30 giây và Refresh, không xóa ảnh |

Hoàn tất khi echo/store Success, ảnh đích tồn tại, có lịch sử, duplicate không tăng bản ghi. Xem [hướng dẫn Prod](HUONG-DAN-PROD-XQUANG.md) trước khi kết nối thiết bị thật.
