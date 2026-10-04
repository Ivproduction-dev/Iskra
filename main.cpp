#include <charconv>
#include <cctype>
#include <fstream>
#include <iostream>
#include <limits>
#include <sstream>
#include <string>
#include <unordered_map>
#include <utility>

namespace {

using Variables = std::unordered_map<std::string, long long>;

std::string lower_keyword(std::string value) {
    for (std::size_t i = 0; i < value.size(); ++i) {
        const auto byte = static_cast<unsigned char>(value[i]);
        if (byte >= 'A' && byte <= 'Z') {
            value[i] = static_cast<char>(std::tolower(byte));
        }
    }

    const std::pair<std::string, std::string> pairs[] = {
        {"А", "а"}, {"Б", "б"}, {"В", "в"}, {"Г", "г"}, {"Д", "д"},
        {"Е", "е"}, {"Ё", "ё"}, {"Ж", "ж"}, {"З", "з"}, {"И", "и"},
        {"Й", "й"}, {"К", "к"}, {"Л", "л"}, {"М", "м"}, {"Н", "н"},
        {"О", "о"}, {"П", "п"}, {"Р", "р"}, {"С", "с"}, {"Т", "т"},
        {"У", "у"}, {"Ф", "ф"}, {"Х", "х"}, {"Ц", "ц"}, {"Ч", "ч"},
        {"Ш", "ш"}, {"Щ", "щ"}, {"Ъ", "ъ"}, {"Ы", "ы"}, {"Ь", "ь"},
        {"Э", "э"}, {"Ю", "ю"}, {"Я", "я"}
    };
    for (const auto& [upper, lower] : pairs) {
        std::size_t position = 0;
        while ((position = value.find(upper, position)) != std::string::npos) {
            value.replace(position, upper.size(), lower);
            position += lower.size();
        }
    }
    return value;
}

bool parse_integer(const std::string& text, long long& result) {
    if (text.empty()) return false;
    const char* begin = text.data();
    const char* end = begin + text.size();
    if (*begin == '+') {
        ++begin;
        if (begin == end || *begin < '0' || *begin > '9') return false;
    }
    const auto parsed = std::from_chars(begin, end, result);
    return parsed.ec == std::errc{} && parsed.ptr == end;
}

bool run_line(const std::string& source, Variables& variables, std::size_t line_number) {
    std::istringstream input(source);
    std::string command;
    if (!(input >> command)) return true;

    const auto keyword = lower_keyword(command);
    const auto fail = [line_number](const std::string& message) {
        std::cerr << "Строка " << line_number << ": " << message << '\n';
        return false;
    };

    if (keyword == "задать" || keyword == "присвоить") {
        std::string name;
        std::string equals;
        std::string value_text;
        std::string extra;
        if (!(input >> name >> equals >> value_text) || equals != "=" || (input >> extra)) {
            return fail("ожидается: Задать имя = число");
        }
        long long value = 0;
        if (!parse_integer(value_text, value)) return fail("ожидается целое число");
        variables[name] = value;
        return true;
    }

    if (keyword == "изменить") {
        std::string name;
        std::string amount_text;
        std::string extra;
        if (!(input >> name >> amount_text) || (input >> extra)) {
            return fail("ожидается: Изменить имя число");
        }
        const auto found = variables.find(name);
        if (found == variables.end()) return fail("переменная «" + name + "» не задана");
        long long amount = 0;
        if (!parse_integer(amount_text, amount)) return fail("ожидается целое число, например 1 или -1");
        const auto current = found->second;
        if ((amount > 0 && current > std::numeric_limits<long long>::max() - amount) ||
            (amount < 0 && current < std::numeric_limits<long long>::min() - amount)) {
            return fail("переполнение целого числа");
        }
        found->second += amount;
        return true;
    }

    if (keyword == "печать") {
        std::string name;
        std::string extra;
        if (!(input >> name) || (input >> extra)) return fail("ожидается: Печать имя");
        const auto found = variables.find(name);
        if (found == variables.end()) return fail("переменная «" + name + "» не задана");
        std::cout << found->second << '\n';
        return true;
    }

    return fail("неизвестная команда «" + command + "»");
}

}

int main(int argc, char* argv[]) {
    if (argc != 2) {
        std::cerr << "Использование: iskra <файл.isk>\n";
        return 2;
    }

    std::ifstream file(argv[1]);
    if (!file) {
        std::cerr << "Не удалось открыть файл: " << argv[1] << '\n';
        return 2;
    }

    Variables variables;
    std::string line;
    std::size_t line_number = 0;
    while (std::getline(file, line)) {
        ++line_number;
        if (line_number == 1 && line.size() >= 3 &&
            static_cast<unsigned char>(line[0]) == 0xEF &&
            static_cast<unsigned char>(line[1]) == 0xBB &&
            static_cast<unsigned char>(line[2]) == 0xBF) {
            line.erase(0, 3);
        }
        if (!run_line(line, variables, line_number)) return 1;
    }
    if (file.bad()) {
        std::cerr << "Ошибка чтения файла\n";
        return 2;
    }
    return 0;
}
