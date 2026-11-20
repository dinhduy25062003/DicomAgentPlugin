# Gói cài đặt Windows và Ubuntu/Debian

Phiên bản hiện tại: **0.2.2**, CPU **x64/amd64**. Host và Desktop đều self-contained .NET 8.

Bản 0.2.2 bổ sung quyền dùng UI ngay sau cài và tùy chọn UI autostart trên cả hai OS; giữ Controller → Application → Infrastructure, API localhost, request JSON theo tenant đầu tiên, chọn/nhận DICOM và Done upload SaaS. [Hướng dẫn luồng mới](LOCALHOST-WEB-SAAS.md). Cài đè giữ ảnh, history, settings và request/tenant binding.

## Windows Setup.exe

Bản Windows **0.1.1** bổ sung SCU tự cài vào `%SystemDrive%\DicomSimulator` (thường `C:\DicomSimulator`), Desktop không mở console và bấm X ẩn xuống system tray. Menu tray có Open và Exit desktop; Exit không dừng service. Vị trí trong nhóm hidden icons do Windows quyết định. Tạo ảnh thử vào TEMP, không ghi vào thư mục binaries của simulator.

Artifact mới: `publish/installers/DicomAgent-0.2.2-win-x64-Setup.exe`.

1. Copy duy nhất file Setup.exe sang máy Windows x64, thoát Desktop Agent đang mở qua menu tray → **Exit desktop** nếu có (bấm X chỉ ẩn).
2. Chạy Setup.exe, chấp nhận elevation để cài service và ACL.
3. Nhập Windows account của người vận hành, dạng `COMPUTER\user` hoặc `DOMAIN\user`. Không nhập mật khẩu. Tài khoản này được thêm vào `DicomAgentOperators`.
4. Setup cài Host/Desktop vào `%ProgramFiles%\DicomAgent`, cài và tự khởi động Windows Service `DicomAgent` bằng identity `NT SERVICE\DicomAgent`.
5. Mở **DICOM Desktop Agent** từ Start Menu ngay sau khi cài; quyền theo SID được cấp trực tiếp nên không cần đăng xuất.
6. Cấu hình AE/IP/port/storage/allowlists trong UI. Restart service để áp dụng. Firewall chưa tự mở; quản trị mở rule LAN chỉ từ IP máy X-quang được phép.

Không cần cài .NET Runtime trên máy khách. Không tách riêng một Desktop.exe khỏi DLL/native files trong thư mục publish; Setup.exe đã chứa tất cả các file này.

Gỡ bằng Settings → Apps → DICOM Desktop Agent hoặc `%ProgramFiles%\DicomAgent\Uninstall.exe`. Uninstaller gỡ service/firewall/shortcut/đăng ký ứng dụng; **giữ lại ảnh, SQLite, cấu hình, tài khoản/group thư mục binaries và thư mục simulator** để tránh xóa storage tùy chỉnh mà khách hàng đặt dưới thư mục cài đặt. Có thể xóa binaries thủ công sau khi xác nhận không có dữ liệu cần giữ.

Gói chưa ký Authenticode. Chưa kiểm thử chạy/cài/gỡ thực tế trên Windows; chỉ cross-build NSIS và kiểm tra payload từ Linux. Cần xác nhận trên Windows VM trước triển khai tại phòng khám.

## Ubuntu/Debian `.deb`

Artifact: `publish/installers/dicom-agent_0.2.2_amd64.deb`.

Tại thư mục chứa file tải về:

```bash
sudo apt install ./dicom-agent_0.2.2_amd64.deb
```

Gói tự nhận tài khoản gọi sudo, cấp ACL cho phiên hiện tại và bật service ngay. Không cần usermod hoặc đăng xuất. Sau đó mở **DICOM Desktop Agent** trong menu ứng dụng hoặc chạy:

```bash
dicom-agent-desktop
systemctl status dicom-agent.service
# Sau khi lưu settings:
sudo systemctl restart dicom-agent.service
```

Gói tự cài binaries vào `/opt/dicom-agent`, systemd unit, Desktop launcher/icon và tài khoản hạn chế `dicom-agent`; tạo `/var/lib/dicom-agent` với group operator và quyền hạn chế. Tài khoản gọi `sudo apt install` được nhận qua SUDO_USER/SUDO_UID để cấp quyền ngay; PKEXEC_UID cũng được hỗ trợ. Với cài đặt root không có người dùng gốc, quản trị có thể chỉ định DICOM_AGENT_OPERATOR_USER. Không tự mở firewall. Data mặc định `/var/lib/dicom-agent/storage`, DB/config nằm trong `/var/lib/dicom-agent`.

Dependencies native X11/font/ICU/OpenSSL được khai báo để `apt` giải quyết. Không cần .NET Runtime. Cần Internet hoặc nguồn apt/cache offline có đủ dependencies nếu máy chưa cài chúng. Sau cài đặt, nhận/lưu DICOM không cần Internet. UI cần phiên đồ họa; Host không cần mở UI. Gói dành cho Ubuntu/Debian amd64 dùng systemd, libc >=2.34; các distro/phiên bản khác cần kiểm thử native dependencies riêng.

Gỡ:

```bash
sudo apt remove dicom-agent
# Hoặc purge metadata package, vẫn giữ ảnh/DB/settings/tài khoản/group:
sudo apt purge dicom-agent
```

Package không chứa file DICOM, DB hay settings khách hàng. remove/purge không tự xóa `/var/lib/dicom-agent` hoặc custom storage. Không cài song song bằng script install.sh và gói deb (unit cài thủ công trong `/etc/systemd/system` có thể ghi đè unit do package quản lý).

## Build lại

Trên Linux:

```bash
bash deploy/linux/publish.sh
bash deploy/linux/build-deb.sh 0.2.2
# Để build Windows installer, cần PowerShell và NSIS trên máy build:
pwsh -NoProfile -File deploy/windows/publish.ps1
# Cài makensis qua package nsis trên máy build nếu chưa có, hoặc đặt MAKENSIS/NSISDIR cho tool unpack cục bộ:
bash deploy/windows/build-installer.sh 0.2.2
```

Trên Windows, cài NSIS cho máy build rồi:

```powershell
./deploy/windows/build-installer.ps1 -Version 0.2.2
# Nếu binaries đã publish:
./deploy/windows/build-installer.ps1 -Version 0.2.2 -SkipPublish
```

NSIS chỉ cần trên máy build, không cần trên máy khách. Công cụ Debian `dpkg-deb` cũng chỉ cần cho build hoặc cài gói deb. SHA-256 của hai artifact nằm trong `publish/installers/SHA256SUMS`.

## Kiểm chứng nền tảng gói 0.1.x

- Build `.deb`, kiểm tra metadata và checksum toàn bộ payload sau extract.
- Parse shell/PowerShell maintainer/setup scripts và validate `.desktop`; `apt-get -s install` giải quyết dependencies thành công trên Ubuntu 26.04 hiện tại (chỉ mô phỏng).
- Chạy Receiver từ binary trích xuất `.deb`: SCU tổng hợp C-ECHO/C-STORE/duplicate, SQLite/file/hash/IPC và SIGTERM.
- Compile Setup.exe bằng NSIS, 7-Zip kiểm tra archive thành công; đối chiếu SHA-256 toàn bộ 218 file Host/Desktop trích xuất với binaries publish.
- Chưa chạy maintainer scripts dưới root, cài/gỡ qua apt/systemd trên VM, hoặc cài/gỡ Windows thực tế. Không thay đổi service/firewall của máy development.

Luồng và kiểm chứng ứng dụng 0.2.2: [LOCALHOST-WEB-SAAS.md](LOCALHOST-WEB-SAAS.md), [VERIFICATION.md](VERIFICATION.md).


Từ 0.2.2, **Cấu hình kết nối → Mở giao diện khi đăng nhập máy → Lưu tùy chọn khởi động** bật/tắt autostart UI cho tài khoản hiện tại trên cả Windows và Linux. Dịch vụ Host vẫn tự chạy khi cài và khi boot, độc lập với UI. Linux cấp ACL readonly theo UID trên data/file kết nối HTTP; Windows cấp SID trực tiếp trên data để phiên hiện tại không cần làm mới group token.
