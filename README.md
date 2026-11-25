# DICOM Desktop Agent

Từ 0.2.2: cài xong mở UI dùng ngay, không cần đăng xuất để nhận quyền; có tùy chọn mở UI khi đăng nhập máy trong Cấu hình kết nối.

Gói cài đặt **Windows Setup.exe** và **Ubuntu/Debian .deb**: xem [PACKAGES.md](docs/PACKAGES.md).

Bản **0.2.15** thêm **Lưu và áp dụng tự động**: chọn thư mục ảnh, bấm Lưu cấu hình, xác thực quản trị nếu được yêu cầu; app tự cấp quyền thư mục và khởi động lại service, không cần chạy lệnh. App hỗ trợ giao diện sáng và tối tông xanh dương: chọn tại **Cấu hình kết nối → Giao diện Desktop → Chủ đề màu**, áp dụng ngay và ghi nhớ riêng cho tài khoản hiện tại; bản 0.2.4 tổ chức lại code cho dễ đọc, dễ debug; chặn tạo trùng request `new` theo `resModel + resId + PartnerId` trong storage, vẫn cho tạo mới khi ca cũ đã `done`; nhận request từ web trên cùng máy qua `http://127.0.0.1:17890`, cố định tenant từ request đầu tiên, lưu ca trong JSON, nhận/chọn DICOM và gửi multipart lên SaaS khi bấm Done. Ba màn hình: quản lý request, thực hiện dịch vụ (có preview frame đầu tiên), cấu hình kết nối. [Hướng dẫn và hợp đồng web → local → SaaS](docs/LOCALHOST-WEB-SAAS.md). Bộ nhận DICOM/lưu ảnh vẫn hoạt động khi không có Internet; không tự chuyển JPEG/PNG.

## Kiến trúc

```text
Máy X-quang -- C-ECHO / C-STORE TCP --> Host (.NET Worker)
                                       |-- Storage SCP (fo-dicom)
                                       |-- Ghi file tạm -> .dcm
                                       |-- SQLite / EF Core
Desktop (Avalonia MVVM) -- HTTP 127.0.0.1 ---|
Web cùng máy -- HTTP loopback + Authorization/TenantName --> Host
Host -- request JSON / phiên nhận ảnh / Done multipart --> SaaS IrAttachments
```

Luồng API: `CaptureRequestsController` (Host) → `ICaptureRequestService` / `CaptureRequestService` (Application) → các interface repository, token store, DICOM preview, upload client và queue (implementation trong Infrastructure). `CaptureUploadWorker` ở Host chạy nghiệp vụ upload nền. Application không tham chiếu Infrastructure, ASP.NET Core hoặc fo-dicom.

Domain chứa entity; Application chứa contracts, validation, đường dẫn và client quản trị. Infrastructure triển khai DICOM, JSON repository, bảo vệ token, HTTP upload, file storage, SQLite, cấu hình và các adapter quản trị HTTP. Host là composition root duy nhất khởi chạy Receiver. Desktop chỉ tham chiếu Application, không dùng fo-dicom/EF và không chiếm DICOM port. Host và Desktop là hai tiến trình độc lập.

```text
src/
  DicomAgent.Domain/          Entities/DicomInstance.cs
  DicomAgent.Application/     Abstractions, Models, Services
  DicomAgent.Infrastructure/  Dicom, Storage, Persistence/Migrations, Configuration, Management
  DicomAgent.Host/            Workers, Program.cs, appsettings.json
  DicomAgent.Desktop/         Views, ViewModels, App.axaml
 tests/
  DicomAgent.UnitTests/
  DicomAgent.IntegrationTests/
 tools/DicomAgent.Scu/        SCU và generator fixture tổng hợp, fo-dicom
 deploy/windows/             install.ps1, uninstall.ps1, publish.ps1
 deploy/linux/               systemd unit, install.sh, uninstall.sh, publish.sh
 docs/                       Triển khai và báo cáo kiểm chứng
```

## Yêu cầu và môi trường phát triển

- .NET SDK **8.0.422** (pin trong `global.json`), C# 12, tất cả project `net8.0`.
- fo-dicom **5.2.6**, EF Core SQLite **8.0.22**, Avalonia **11.3.7**, xUnit. Phiên bản quản lý tập trung trong `Directory.Packages.props`.
- Dependency vá được pin riêng: SQLitePCLRaw.bundle_e_sqlite3 **2.1.14**, Tmds.DBus.Protocol **0.21.3**.
- Windows x64 hoặc Linux x64. UI Linux cần phiên desktop X11/Wayland, các thư viện đồ họa/font của Avalonia (X11, Fontconfig, libICE/libSM, libSkiaSharp prerequisites).
- NuGet/Internet chỉ cần lúc restore/build/publish, không cần để nhận ảnh lúc chạy.
- Không cần Docker, SQL Server hoặc PostgreSQL. Bản self-contained không cần cài .NET Runtime trên máy khách; vẫn cần thư viện native của hệ điều hành.

```bash
dotnet --version
dotnet restore DicomAgent.sln
dotnet build DicomAgent.sln -c Release --no-restore
dotnet test DicomAgent.sln -c Release --no-build
```

Trên môi trường hạn chế process/MSBuild, thêm `-m:1 -p:UseSharedCompilation=false` lúc build/test. Test runner và integration tests cần quyền tạo cổng TCP loopback; sandbox chặn TCP không phải lỗi Receiver.

## Chạy trực tiếp trên Ubuntu

Tại root repository, terminal thứ nhất:

```bash
export DICOM_AGENT_DATA="$PWD/.data"
mkdir -p "$DICOM_AGENT_DATA"
chmod 750 "$DICOM_AGENT_DATA"
umask 027
dotnet run --project src/DicomAgent.Host
```

Terminal thứ hai, dùng cùng data directory và tài khoản:

```bash
export DICOM_AGENT_DATA="$PWD/.data"
dotnet run --project src/DicomAgent.Desktop
```

Ctrl+C terminal Host để dừng graceful. Trên Linux, đóng Desktop chỉ đóng UI; trên Windows, bấm X ẩn UI xuống system tray. Menu tray Open mở lại, Exit desktop thoát giao diện; Host service tiếp tục chạy. Chạy CLI Host không cần systemd hoặc phiên đồ họa.

Trong development Windows PowerShell:

```powershell
$env:DICOM_AGENT_DATA = Join-Path $PWD '.data'
dotnet run --project src/DicomAgent.Host
# Terminal khác, cùng biến DICOM_AGENT_DATA:
dotnet run --project src/DicomAgent.Desktop
```

Khi chạy từ source, Host và Desktop phải dùng cùng DICOM_AGENT_DATA. Host tự ghi desktop-connection.json để Desktop gọi HTTP đúng port; sau cài đặt file này được cấp quyền đọc cho operator.

## Cấu hình Receiver

Mặc định trong `src/DicomAgent.Host/appsettings.json`:

```json
{
  "Dicom": {
    "AeTitle": "DICOM_AGENT",
    "ListenAddress": "0.0.0.0",
    "Port": 11112,
    "StoragePath": "",
    "MaxConcurrentAssociations": 10,
    "MaxInstanceBytes": 268435456,
    "AllowedCallingAeTitles": [],
    "AllowedRemoteAddresses": []
  },
  "Agent": { "EnableReceiver": true, "EnableFileHistory": true }
}
```

Thứ tự ưu tiên: default `appsettings.json` cạnh binary → `settings.json` trong application data → biến môi trường có prefix `DICOM_AGENT_` → CLI arguments. Ví dụ `DICOM_AGENT_Dicom__Port=11113` hoặc `--Dicom:Port 11113`. Cấu hình do UI lưu không ghi vào thư mục binary. Nếu môi trường/CLI ghi đè field, chúng vẫn có ưu tiên sau restart.

AE Title: 1–16 ký tự ASCII printable, không backslash, không khoảng trắng đầu/cuối. ListenAddress là IP literal. Port 1–65535; nên dùng port >1024 với service không đặc quyền. Allowlist IP nhận từng IPv4/IPv6 literal, không CIDR/hostname. AE allowlist so sánh phân biệt hoa/thường. Danh sách rỗng cho phép mọi thiết bị có thể truy cập DICOM port; cấu hình allowlist và firewall trước khi sử dụng thực tế.

`StoragePath` rỗng dùng mặc định OS; đường dẫn tùy chỉnh phải tuyệt đối, không parent traversal hoặc symlink. Service cần quyền ghi; Desktop/operator cần quyền đọc để chọn upload. `MaxConcurrentAssociations`: 1–100; `MaxInstanceBytes`: 1 KiB–2 GiB, áp dụng ngay khi nhận stream C-STORE, mặc định 256 MiB. PDU 16 KiB, timeout DIMSE request 30 giây. Timeout này không phải giới hạn tổng thời gian truyền ảnh hay idle timeout tuyệt đối cho mọi loại association.

**Lưu trên Desktop tự áp dụng cấu hình từ 0.2.8.** UI gọi helper Host đã cài qua polkit/UAC để cấp quyền thư mục, lưu và restart service, rồi tự kết nối lại. Receiver vẫn giữ snapshot lúc khởi động; không hot-mutate cấu hình giữa các association. API management `save` cũ vẫn chỉ lưu và báo cần restart.

## DICOM conformance của MVP

- C-ECHO SCP, C-STORE SCP; kiểm tra Called AE, Calling AE và IP nguồn.
- Transfer Syntax được accept: Explicit VR Little Endian `1.2.840.10008.1.2.1` và Implicit VR Little Endian `1.2.840.10008.1.2`.
- Không advertise JPEG/JPEG-LS/JPEG 2000, RLE hoặc Big Endian; không tự transcode. Cấu hình modality gửi uncompressed Little Endian.
- Storage SOP: CR; DX presentation/processing; mammography presentation/processing; intraoral X-ray presentation/processing; CT/enhanced CT; MR/enhanced MR; secondary capture. Danh sách UID cụ thể trong `StorageScp.cs`.
- Không hỗ trợ C-FIND, C-MOVE, Storage Commitment, DICOMweb hay TLS trong MVP.
- Dataset và Pixel Data được serialize thành Part 10 bằng fo-dicom, giữ Transfer Syntax. Không tuyên bố byte-for-byte giống file đầu vào: network DIMSE không gửi toàn bộ Part 10 header và thư viện có thể serialize lại encoding.
- Thiếu UID study/series/SOP/class hoặc command/dataset không khớp sẽ bị từ chối. Optional tags có thể null.
- Ghi thành công: C-STORE Success. Lỗi ghi/thiếu quyền: Storage Out of Resources. Dataset không hiểu: Cannot Understand. UID cùng nội dung khác: Processing Failure, giữ file cũ. SQLite lỗi sau khi file đã commit: vẫn trả Success vì file đã được giữ; Dashboard/log báo lịch sử chờ reconciliation.

## Lưu file, duplicate và phục hồi

Mặc định:

- Windows: `C:\ProgramData\DicomAgent\Storage`.
- Linux: `/var/lib/dicom-agent/storage`.
- SQLite: `history.db` trong thư mục data (`C:\ProgramData\DicomAgent` hoặc `/var/lib/dicom-agent`). `DICOM_AGENT_DATA` đổi vị trí toàn bộ data cho development; `StoragePath` có thể đổi riêng ảnh.

```text
Storage/YYYY-MM-DD/StudyInstanceUID/SeriesInstanceUID/SOPInstanceUID.dcm
```

Ngày là ngày nhận UTC. Không đưa tên bệnh nhân vào đường dẫn. UID chỉ chứa chữ số/dấu chấm hợp lệ, dài tối đa 64 ký tự. Không dùng đường dẫn từ dataset ngoài các UID đã validate.

File được ghi vào `.partial` cùng filesystem, flush trước khi move nguyên tử sang tên chính thức. Chỉ `.dcm` đã hoàn tất được đưa vào history. Duplicate lookup trên toàn storage root (kể cả ngày cũ); SHA-256 kiểm tra file, khi Part 10 header khác sẽ so sánh canonical dataset + transfer syntax, giữ nguyên file đã lưu. Không overwrite khi cùng UID khác nội dung. EF unique index `(StorageRoot, SopInstanceUid)` ngăn duplicate record; mỗi lần insert là transaction SQLite. Một semaphore đồng bộ commit/reconciliation trong Host.

Nếu SQLite lỗi, file đã commit không bị xóa. Host thử migrate/reconcile lúc khởi động và mỗi 30 giây; chỉ parse/hash các file chưa có trong history. Có thể rebuild history từ `.dcm` ở storage root hiện hành nếu DB được phục hồi. Sao lưu cả storage, DB và settings trước bảo trì; nếu DB/schema hỏng vật lý cần khôi phục SQLite rồi reconciliation. Ảnh không tự xóa. `.partial` còn sót sau crash không được tự nhận là file hoàn chỉnh; giữ để điều tra/phục hồi thủ công. Không cam kết chống mất điện ở cấp phần cứng/filesystem.

Một Host sở hữu một data directory và storage root; không chạy hai installation khác data directory nhưng dùng chung storage root. Khi chuyển storage root, file cũ vẫn giữ nguyên và lịch sử cũ vẫn trong DB; reconciliation tự động chỉ quét root hiện hành. Tác vụ dò duplicate vẫn quét tên file dưới root, vì vậy storage rất lớn cần tối ưu catalog về sau.

## Sử dụng Desktop

1. Cài/chạy Host trước. Mở Desktop bằng operator đã được cấp quyền.
2. **Quản lý request**: request từ web tự cập nhật, tìm khách/đối tượng/dịch vụ, lọc trạng thái, bấm tên xem chi tiết, Thực hiện hoặc xóa có xác nhận.
3. **Thực hiện dịch vụ**: bật nhận DICOM cho một ca, hoặc chọn file/thư mục; xem danh sách ảnh và preview frame đầu tiên, đối chiếu PatientName/PatientID/Study, bỏ ảnh không dùng và bấm Done để upload SaaS.
4. **Cấu hình kết nối**: trạng thái receiver, AE/IP/port/storage/allowlists, dung lượng/kết nối tối đa và API localhost/SaaS. Bấm Lưu cấu hình, xác thực quản trị nếu được yêu cầu; app tự restart service và kết nối lại; Làm mới sẽ nạp lại các trường cấu hình từ Host.
5. Tenant từ request đầu tiên được giữ cố định qua restart; app không cần login riêng. [Contract, mẫu fetch và các trạng thái lỗi upload](docs/LOCALHOST-WEB-SAAS.md).
6. Trên Windows, bấm X đưa UI xuống system tray; menu Exit desktop thoát giao diện. Đóng UI không dừng Host hoặc API localhost.

Web và Desktop đều gọi HTTP tại `127.0.0.1:17890`; không có Unix socket, named pipe hoặc WebSocket. Web dùng `/api/capture-requests` với Authorization/TenantName; Desktop dùng `/api/management/*` với khóa local tự tạo trong `desktop-connection.json`. Endpoint quản trị không cho browser Origin truy cập; file kết nối chỉ đọc được bởi operator, admin và service. Cài bằng sudo/Setup tự cấp quyền ngay cho phiên hiện tại.


## Cài Windows

Setup Windows 0.2.15 tự cài SCU vào `%SystemDrive%\DicomSimulator` (thường `C:\DicomSimulator`). Desktop không mở terminal; bấm X ẩn xuống tray, menu Open mở lại và Exit desktop thoát giao diện. Trước nâng cấp, chọn Exit desktop thay vì chỉ bấm X. Chi tiết kiểm thử: [Windows](docs/HUONG-DAN-WINDOWS.md).


PowerShell tại root repository:

```powershell
./deploy/windows/publish.ps1
# Terminal Administrator, thay operator và IP bằng thông tin phòng khám:
./deploy/windows/install.ps1 -OperatorUser 'CLINIC\operator' -AllowedRemoteAddresses @('192.168.1.20') -Port 11112
Get-Service DicomAgent
Restart-Service DicomAgent
# Chạy bằng operator trong phiên desktop:
& "$env:ProgramFiles/DicomAgent/desktop/DicomAgent.Desktop.exe"
```

Host chạy identity ảo `NT SERVICE\DicomAgent`, automatic startup, không dùng LocalSystem để nhận DICOM. Installer bảo vệ data bằng ACL; operator được cấp quyền đọc data trực tiếp theo SID và ghi cấu hình qua HTTP localhost, không cần đăng xuất. Firewall chỉ được thêm khi có explicit IP allowlist, chỉ profile Domain/Private; không tự mở Public. Nếu dùng port/IP khác cấu hình hiện có, kiểm tra settings và firewall khớp nhau; installer giữ settings cũ lúc upgrade. Custom storage cần cấp Modify cho service và Read cho operator.

```powershell
./deploy/windows/uninstall.ps1
```

Gỡ service/firewall, giữ ảnh, DB, settings, binaries và group. Chi tiết đóng gói MSI/EXE: [DEPLOYMENT.md](docs/DEPLOYMENT.md).

## Cài Linux/systemd

```bash
bash deploy/linux/publish.sh
# Thay clinicuser bằng người sẽ dùng Desktop:
sudo bash deploy/linux/install.sh ./publish/linux clinicuser
systemctl status dicom-agent.service
sudo systemctl restart dicom-agent.service
journalctl -u dicom-agent.service -f
/opt/dicom-agent/desktop/DicomAgent.Desktop
```

Installer tạo user hệ thống `dicom-agent`, group `dicom-agent-operators`, data mode `2750`, systemd auto-start. Đăng xuất/đăng nhập lại để group có hiệu lực. Unit giới hạn quyền ghi ở `/var/lib/dicom-agent`, bảo vệ home và filesystem; custom storage cần thêm `ReadWritePaths` bằng `systemctl edit dicom-agent.service`, quyền owner/group phù hợp và restart.

Firewall không tự mở. Ví dụ UFW, chỉ từ máy X-quang:

```bash
sudo ufw allow from 192.168.1.20 to any port 11112 proto tcp
# Bật/tùy chỉnh UFW theo chính sách mạng đang dùng; không bật mù quáng trên máy quản trị từ xa.
sudo bash deploy/linux/uninstall.sh
```

Uninstall giữ dữ liệu, tài khoản/group và binaries. Không chạy installer trên máy development nếu chỉ cần test Receiver.

## Cấu hình máy X-quang và test từ máy khác

Máy Agent đặt IP LAN cố định/reservation, ví dụ `192.168.1.10`. Trên modality cấu hình destination:

| Field | Ví dụ |
|---|---|
| Remote AE / Called AE | `DICOM_AGENT` |
| Remote host | `192.168.1.10` |
| TCP port | `11112` |
| Modality Calling AE | `XRAY_ROOM_1` (trùng allowlist) |
| Transfer Syntax | Explicit hoặc Implicit VR Little Endian, uncompressed |

Trước tiên xác nhận hai máy liên lạc LAN, allowlist và firewall đúng IP/AE. SCU tool trong repository:

```bash
# Tạo ảnh DX tổng hợp 2x2 (không có dữ liệu bệnh nhân thật):
dotnet run --project tools/DicomAgent.Scu -- generate /tmp/synthetic.dcm
# C-ECHO từ máy đã cài SDK / có source:
dotnet run --project tools/DicomAgent.Scu -- echo 192.168.1.10 11112 DICOM_AGENT XRAY_ROOM_1
# C-STORE, chỉ dùng fixture tổng hợp hoặc dữ liệu đã được phép xử lý:
dotnet run --project tools/DicomAgent.Scu -- store 192.168.1.10 11112 DICOM_AGENT /path/to/synthetic.dcm XRAY_ROOM_1
```

Hoặc DCMTK nếu máy test có `echoscu` / `storescu`:

```bash
echoscu -aet XRAY_ROOM_1 -aec DICOM_AGENT 192.168.1.10 11112
storescu -aet XRAY_ROOM_1 -aec DICOM_AGENT 192.168.1.10 11112 /path/to/synthetic.dcm
```

Xác nhận response Success, file xuất hiện đúng UID trong storage, history có đúng một record, mở file bằng DICOM viewer. Gửi lại file phải không tạo thêm record; sửa Pixel Data nhưng giữ UID phải bị từ chối. Automated tests tự tạo ảnh DX 2×2 tổng hợp, không dùng dữ liệu bệnh nhân thật. Các công cụ DICOM khác không phải dependency runtime của Agent.

## Test và migration

```bash
dotnet test DicomAgent.sln -c Release --logger trx
# Nếu cần tạo migration mới, dùng dotnet-ef 8.x:
dotnet ef migrations add MigrationName --project src/DicomAgent.Infrastructure --startup-project src/DicomAgent.Infrastructure --output-dir Persistence/Migrations
```

Smoke test artifact Linux sau khi publish (cần Python 3 và đã build SCU Release):

```bash
dotnet build tools/DicomAgent.Scu -c Release
python3 tools/smoke-published-linux.py
```

Script dùng cổng loopback/dữ liệu tổng hợp tạm thời, chạy Host self-contained, C-ECHO/C-STORE/duplicate, kiểm tra DB/hash/HTTP rồi SIGTERM; không cài service.

Host áp dụng migration khi khởi động. Migration `InitialHistory` và snapshot có trong source, không dùng `EnsureCreated` cho production. Xem [báo cáo kiểm chứng](docs/VERIFICATION.md) để biết môi trường và ranh giới đã/chưa test. CI có matrix Windows/Linux, nhưng workflow chỉ được thực thi khi repository được đưa lên GitHub.

## Publish

```bash
bash deploy/linux/publish.sh
# Cross-publish Windows từ Linux nếu cần:
pwsh -NoProfile -File deploy/windows/publish.ps1
```

Hoặc lệnh trực tiếp:

```bash
dotnet publish src/DicomAgent.Host -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/linux/host
dotnet publish src/DicomAgent.Desktop -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=false -o publish/linux/desktop
dotnet publish src/DicomAgent.Host -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/windows/host
dotnet publish src/DicomAgent.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish/windows/desktop
```

Host single-file có native SQLite được extract; service Linux cấu hình extraction trong data directory. Desktop giữ thư viện/native assets đi kèm, không ép single-file. Không bật trimming/AOT vì EF/fo-dicom/Avalonia cần kiểm chứng riêng. Linux artifact không chạy trên Windows; cross-publish không chứng minh Windows runtime đã được test.

## Troubleshooting

| Hiện tượng | Kiểm tra |
|---|---|
| Port bị chiếm | Linux `ss -ltnp`, Windows `Get-NetTCPConnection -LocalPort 11112`; đổi port hoặc dừng tiến trình đúng chủ sở hữu. Host fail fast; systemd restart có rate limit. |
| Association rejected | Called AE có đúng case; Calling AE và IP có trong allowlists; Transfer Syntax/SOP có được hỗ trợ. |
| Không kết nối được | ListenAddress là interface tồn tại, firewall LAN và IP destination; không dùng `127.0.0.1` khi nhận từ máy khác. |
| C-STORE thất bại | Quyền ghi, dung lượng đĩa, UID bắt buộc, size limit, cùng UID khác dữ liệu. Không xóa ảnh cũ để thử lại. |
| File có, history thiếu | Xem log, phục hồi quyền/schema DB hoặc dung lượng; chờ reconciliation 30 giây/restart. File hoàn chỉnh được giữ. |
| UI disconnected | Host đang chạy, cùng `DICOM_AGENT_DATA`, quyền đọc desktop-connection.json và port HTTP local; không chạy UI dưới identity không được cấp quyền. |
| Settings chưa áp dụng | Bấm Lưu cấu hình, chấp nhận xác thực quản trị và đợi kết nối lại; kiểm tra biến môi trường/CLI có ghi đè. |
| Custom storage bị chặn trên Linux | systemd `ProtectHome`/`ReadWritePaths`, owner/group và mount permissions; không giảm toàn bộ sandbox nếu chỉ cần thêm một thư mục. |
| UI Linux không mở | Phiên đồ họa và native libraries/font phù hợp distro; service Host không cần X11/Wayland. |

## Bảo mật và giới hạn MVP

DICOM TCP thông thường **không tự có TLS**. Chỉ sử dụng trên LAN đáng tin cậy, VLAN/VPN được bảo vệ; không NAT/publish DICOM port ra Internet. IP/AE allowlist không phải xác thực mật mã, không thay TLS. Hạn chế truy cập vật lý, dùng mã hóa ổ đĩa (BitLocker/LUKS) và chính sách sao lưu/retention của phòng khám; Agent không tự xóa ảnh. SQLite chứa PatientID/PatientName và metadata y tế; bảo vệ cả DB, WAL/SHM, storage, receive-temp và backups. Log mặc định không ghi PatientID/PatientName hoặc dataset; lỗi dashboard được tóm tắt an toàn, chỉ giữ trong RAM của phiên hiện tại.

Không lưu mật khẩu. Authorization của request được mã hóa và giữ theo từng ca, chỉ chuyển tiếp tới SaaS đã cấu hình khi người dùng bấm Done; xem [luồng localhost](docs/LOCALHOST-WEB-SAAS.md). Operator group là quyền quản trị cục bộ mạnh. Installation scripts cần admin/root để cài service/ACL, service chạy bằng tài khoản hạn chế. Runtime cần được cập nhật bản vá định kỳ theo chính sách .NET và vendor; package versions pin không phải bảo đảm đã qua audit bảo mật.

Đây là implementation MVP kỹ thuật, chưa được kiểm chứng với modality thật hay quy trình lâm sàng. Cần kiểm thử chấp nhận tại phòng khám, Windows service, UI/native dependencies, thiết bị và DICOM viewer thực tế trước triển khai.

## Mở rộng Website Integration

Bản 0.2.0 có request JSON, upload thủ công qua Done, phục hồi trạng thái và quy trình đối chiếu khi kết quả upload chưa rõ. Upload tách khỏi response C-STORE; nhận ảnh không phụ thuộc Internet. Refresh JWT, đối chiếu receipt từng file và idempotency ở phía SaaS cần hợp đồng backend bổ sung; không tự retry khi có thể tạo ảnh trùng.

## Nguồn API

API được đối chiếu thêm với XML documentation/assembly từ package đã restore và build/test thực tế:
[fo-dicom repository](https://github.com/fo-dicom/fo-dicom),
[fo-dicom v5 API](https://fo-dicom.github.io/dev/v5/api/FellowOakDicom.Network.IDicomServerFactory.html).

Build cả hai bộ cài bằng một lệnh trên Linux: `bash deploy/release.sh` (tự tăng patch, build/test, publish, kiểm tra payload và ghi checksum). Kết quả trong `publish/installers/`. Chi tiết: [PACKAGES.md](docs/PACKAGES.md).
